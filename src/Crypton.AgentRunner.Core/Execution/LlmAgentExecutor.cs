using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using Crypton.Tools;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace AgentRunner.Execution;

/// <summary>
/// Drives a single LLM agent invocation using Ollama's native tool-calling protocol.
/// All registered tools are passed to the model as <see cref="ChatOptions.Tools"/>;
/// the model responds with <see cref="FunctionCallContent"/> items which are dispatched
/// (in parallel when the model issues multiple calls at once) via
/// <see cref="IVirtualTerminal"/>.  Results are appended as <see cref="ChatRole.Tool"/>
/// messages and the loop repeats until the model produces a turn with no tool calls.
/// </summary>
public sealed class LlmAgentExecutor : IAgentExecutor
{
    private readonly IChatClient _chatClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly IVirtualTerminal _virtualTerminal;
    private readonly IEventLogger _logger;
    private readonly AgentRunnerConfig _config;

    public LlmAgentExecutor(
        IChatClient chatClient,
        IToolRegistry toolRegistry,
        IVirtualTerminal virtualTerminal,
        IEventLogger logger,
        AgentRunnerConfig config)
    {
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _virtualTerminal = virtualTerminal;
        _logger = logger;
        _config = config;
    }

    public async Task<AgentOutput> ExecuteAsync(
        AgentInput input,
        IAgentEventSink sink,
        CancellationToken ct = default)
    {
        var agentSettings = _config.GetAgentSettings(input.AgentName);
        var numCtx = agentSettings.NumCtx ?? _config.Ollama.NumCtx;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, input.SystemPrompt),
            new(ChatRole.User, input.UserPrompt),
        };

        // Build one AIFunction per registered tool — schema exposed to the LLM
        var aiTools = _toolRegistry.GetAllDescriptions()
            .Where(t => _toolRegistry.TryGetRegistration(t.Name, out _))
            .Select(t =>
            {
                _toolRegistry.TryGetRegistration(t.Name, out var reg);
                return (AITool)new ToolAIFunction(reg!, _virtualTerminal);
            })
            .ToList();

        var toolOptions = BuildChatOptions(agentSettings, numCtx, tools: aiTools);
        var finalOptions = BuildChatOptions(agentSettings, numCtx, tools: null);

        var countingSink = new CountingEventSink(sink, _logger, input.AgentName, input.CycleId);
        var diagnostics = Stopwatch.StartNew();
        var iterations = 0;
        string? lastTextOutput = null;
        string? errorMessage = null;
        var success = false;

        try
        {
            // ── Tool-call loop ────────────────────────────────────────────────
            while (iterations < agentSettings.MaxIterations && !ct.IsCancellationRequested)
            {
                iterations++;

                // Stream one turn, collecting text tokens and any tool calls
                var text = new StringBuilder();
                var contentParts = new List<AIContent>();

                await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, toolOptions, ct))
                {
                    foreach (var part in update.Contents)
                    {
                        if (part is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                        {
                            text.Append(tc.Text);
                            countingSink.Publish(new TokenReceivedEvent(tc.Text, input.AgentName));
                        }
                        contentParts.Add(part);
                    }
                }

                messages.Add(new ChatMessage(ChatRole.Assistant, contentParts));

                var toolCalls = contentParts.OfType<FunctionCallContent>().ToList();

                if (toolCalls.Count == 0)
                {
                    // No tool calls — the model has finished, capture text as last output candidate
                    lastTextOutput = text.ToString().Trim();
                    break;
                }

                // Execute all tool calls in parallel
                var toolTasks = toolCalls.Select(tc => ExecuteToolCallAsync(tc, input, countingSink, ct));
                var toolResults = await Task.WhenAll(toolTasks);

                // Append one tool-result message per call
                foreach (var result in toolResults)
                    messages.Add(new ChatMessage(ChatRole.Tool, [result]));

                iterations += toolCalls.Count;
            }

            // ── Final output pass ─────────────────────────────────────────────
            // If the loop ended due to tool calls on the last iteration (no text captured),
            // ask the model for the final output document in plain text.
            if (string.IsNullOrWhiteSpace(lastTextOutput) && !ct.IsCancellationRequested)
            {
                iterations++;
                messages.Add(new ChatMessage(ChatRole.User,
                    "You have finished calling tools. Now produce your final output document. " +
                    "Do NOT include any JSON or tool call markers — write plain text/markdown only."));

                lastTextOutput = await StreamTextAsync(messages, finalOptions, countingSink, input.AgentName, ct);
            }

            success = !string.IsNullOrWhiteSpace(lastTextOutput);
            if (!success)
                errorMessage = "Agent produced an empty output.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        diagnostics.Stop();

        _logger.LogInvocationManifest(input.AgentName, input.CycleId, new InvocationManifest(
            Model: agentSettings.Model,
            Temperature: agentSettings.Temperature,
            NumCtx: numCtx,
            IterationsUsed: iterations,
            MaxIterations: agentSettings.MaxIterations,
            DurationMs: diagnostics.ElapsedMilliseconds,
            Success: success,
            Error: errorMessage));

        return new AgentOutput(success, lastTextOutput, errorMessage, iterations);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<FunctionResultContent> ExecuteToolCallAsync(
        FunctionCallContent toolCall,
        AgentInput input,
        CountingEventSink sink,
        CancellationToken ct)
    {
        var toolName = toolCall.Name;
        var argsJson = JsonSerializer.Serialize(
            toolCall.Arguments ?? new Dictionary<string, object?>());

        var eventId = Guid.NewGuid();
        sink.Publish(new ToolCallStartedEvent(eventId, toolName, argsJson, input.AgentName));

        var sw = Stopwatch.StartNew();
        var result = await _virtualTerminal.RunAsync(toolName, argsJson, ct);
        sw.Stop();

        sink.Publish(new ToolCallCompletedEvent(
            eventId, toolName, result.Output, sw.Elapsed,
            IsError: !result.IsSuccess, input.AgentName));

        return new FunctionResultContent(toolCall.CallId, toolName, result.Output);
    }

    private async Task<string> StreamTextAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        IAgentEventSink sink,
        string agentName,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct))
        {
            if (update.Text is { Length: > 0 } token)
            {
                sb.Append(token);
                sink.Publish(new TokenReceivedEvent(token, agentName));
            }
        }
        return sb.ToString().Trim();
    }

    private static ChatOptions BuildChatOptions(
        AgentSettings agentSettings, int numCtx, IList<AITool>? tools)
    {
        var opts = new ChatOptions
        {
            ModelId = agentSettings.Model,
            Temperature = (float)agentSettings.Temperature,
            MaxOutputTokens = agentSettings.MaxTokens,
            Tools = tools,
        };
        opts.AddOllamaOption(OllamaOption.NumCtx, numCtx);
        return opts;
    }

    // ─── CountingEventSink ────────────────────────────────────────────────────

    /// <summary>
    /// Wraps an <see cref="IAgentEventSink"/>, forwards all events, and additionally logs
    /// tool executions to <see cref="IEventLogger"/> when tool call pairs are observed.
    /// </summary>
    public sealed class CountingEventSink : IAgentEventSink
    {
        private readonly IAgentEventSink _inner;
        private readonly IEventLogger _logger;
        private readonly string _agentName;
        private readonly string _cycleId;
        private readonly Dictionary<Guid, string> _pendingInputs = new();

        public int ToolCallCount { get; private set; }

        public CountingEventSink(
            IAgentEventSink inner, IEventLogger logger, string agentName, string cycleId)
        {
            _inner = inner;
            _logger = logger;
            _agentName = agentName;
            _cycleId = cycleId;
        }

        public void Publish(AgentEvent evt)
        {
            _inner.Publish(evt);

            switch (evt)
            {
                case ToolCallStartedEvent started:
                    ToolCallCount++;
                    _pendingInputs[started.Id] = started.InputJson;
                    break;

                case ToolCallCompletedEvent completed:
                    var input = _pendingInputs.TryGetValue(completed.Id, out var stored) ? stored : "{}";
                    _pendingInputs.Remove(completed.Id);
                    var elapsedMs = (long)completed.Duration.TotalMilliseconds;
                    _logger.LogToolExecution(completed.ToolName, input, elapsedMs);
                    _logger.LogToolCallJournal(_agentName, _cycleId, ToolCallCount,
                        completed.ToolName, input, completed.Output, elapsedMs);
                    break;
            }
        }
    }
}


/// <summary>
/// Drives a single LLM agent invocation using a two-phase tool-call protocol:
/// <list type="number">
///   <item><b>Phase 1 — Tool selection:</b> the agent responds with
///     <c>{"tool": "name"}</c> to call a tool, or <c>{"tool": null}</c> when done.</item>
///   <item><b>Phase 2 — Args elicitation:</b> the executor injects the selected tool's
///     JSON Schema; the agent responds with <c>{"args": {...}}</c>.</item>
/// </list>
/// The final output document is the last non-JSON text response after all tool
/// calls are complete.
/// </summary>
public sealed class LlmAgentExecutor : IAgentExecutor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private static readonly JsonDocumentOptions JsonDocOpts = new() { AllowTrailingCommas = true };

    private readonly IChatClient _chatClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly IVirtualTerminal _virtualTerminal;
    private readonly IEventLogger _logger;
    private readonly AgentRunnerConfig _config;

    public LlmAgentExecutor(
        IChatClient chatClient,
        IToolRegistry toolRegistry,
        IVirtualTerminal virtualTerminal,
        IEventLogger logger,
        AgentRunnerConfig config)
    {
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _virtualTerminal = virtualTerminal;
        _logger = logger;
        _config = config;
    }

    public async Task<AgentOutput> ExecuteAsync(
        AgentInput input,
        IAgentEventSink sink,
        CancellationToken ct = default)
    {
        var agentSettings = _config.GetAgentSettings(input.AgentName);
        var numCtx = agentSettings.NumCtx ?? _config.Ollama.NumCtx;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, input.SystemPrompt),
            new(ChatRole.User, input.UserPrompt),
        };

        var baseOptions = BuildChatOptions(agentSettings, numCtx, jsonFormat: false);
        var jsonOptions = BuildChatOptions(agentSettings, numCtx, jsonFormat: true);

        var countingSink = new CountingEventSink(sink, _logger, input.AgentName, input.CycleId);
        var diagnostics = Stopwatch.StartNew();
        var iterations = 0;
        string? lastTextOutput = null;
        string? errorMessage = null;
        var success = false;

        try
        {
            // ── Tool-call loop ────────────────────────────────────────────────
            while (iterations < agentSettings.MaxIterations && !ct.IsCancellationRequested)
            {
                iterations++;

                // Phase 1: ask the LLM which tool it wants (or null to finish)
                var phase1Response = await StreamTextAsync(messages, jsonOptions, countingSink, input.AgentName, ct);
                messages.Add(new ChatMessage(ChatRole.Assistant, phase1Response));

                var phase1Result = ParsePhase1(phase1Response);

                if (phase1Result is null)
                {
                    // No more tool calls — ask for the final output document
                    break;
                }

                var toolName = phase1Result.ToolName;

                if (!_toolRegistry.TryGetRegistration(toolName, out _))
                {
                    messages.Add(new ChatMessage(ChatRole.User,
                        $"Error: tool '{toolName}' is not available. Choose a tool from the list."));
                    continue;
                }

                string argsJson;

                if (phase1Result.InlineArgsJson is not null)
                {
                    // LLM already provided arguments inline in Phase 1 — skip Phase 2
                    argsJson = phase1Result.InlineArgsJson;
                }
                else
                {
                    // Phase 2: inject the schema, collect args
                    var schema = _toolRegistry.GetJsonSchema(toolName);
                    messages.Add(new ChatMessage(ChatRole.User,
                        $"Call to `{toolName}`. Provide the arguments as JSON conforming to this schema:\n```json\n{schema}\n```"));

                    iterations++;
                    if (iterations >= agentSettings.MaxIterations) break;

                    argsJson = await StreamTextAsync(messages, jsonOptions, countingSink, input.AgentName, ct);
                    messages.Add(new ChatMessage(ChatRole.Assistant, argsJson));
                }

                // Execute the tool
                var eventId = Guid.NewGuid();
                countingSink.Publish(new ToolCallStartedEvent(eventId, toolName, argsJson, input.AgentName));

                var sw = Stopwatch.StartNew();
                var toolResult = await _virtualTerminal.RunAsync(toolName, argsJson, ct);
                sw.Stop();

                countingSink.Publish(new ToolCallCompletedEvent(
                    eventId, toolName, toolResult.Output, sw.Elapsed,
                    IsError: !toolResult.IsSuccess, input.AgentName));

                messages.Add(new ChatMessage(ChatRole.User,
                    $"Tool `{toolName}` result (exit {toolResult.ExitCode}):\n{toolResult.Output}"));
            }

            // ── Final output ──────────────────────────────────────────────────
            ct.ThrowIfCancellationRequested();

            messages.Add(new ChatMessage(ChatRole.User,
                "You have finished calling tools. Now produce your final output document. " +
                "Do NOT include any JSON or tool call markers — write plain text/markdown only."));

            iterations++;
            lastTextOutput = await StreamTextAsync(messages, baseOptions, countingSink, input.AgentName, ct);
            success = !string.IsNullOrWhiteSpace(lastTextOutput);

            if (!success)
                errorMessage = "Agent produced an empty output.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        diagnostics.Stop();

        _logger.LogInvocationManifest(input.AgentName, input.CycleId, new InvocationManifest(
            Model: agentSettings.Model,
            Temperature: agentSettings.Temperature,
            NumCtx: numCtx,
            IterationsUsed: iterations,
            MaxIterations: agentSettings.MaxIterations,
            DurationMs: diagnostics.ElapsedMilliseconds,
            Success: success,
            Error: errorMessage));

        return new AgentOutput(success, lastTextOutput, errorMessage, iterations);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> StreamTextAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        IAgentEventSink sink,
        string agentName,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct))
        {
            if (update.Text is { Length: > 0 } token)
            {
                sb.Append(token);
                sink.Publish(new TokenReceivedEvent(token, agentName));
            }
        }
        return sb.ToString().Trim();
    }

    private sealed record Phase1Result(string ToolName, string? InlineArgsJson);

    /// <summary>
    /// Parses a Phase 1 response. Returns a <see cref="Phase1Result"/> containing the tool
    /// name and optionally inline arguments if the LLM already provided them. Returns null
    /// when the LLM signals it is done (no tool) or the response cannot be parsed.
    /// </summary>
    private static Phase1Result? ParsePhase1(string response)
    {
        var json = ExtractJson(response);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json, JsonDocOpts);
            if (!doc.RootElement.TryGetProperty("tool", out var toolProp))
                return null;

            if (toolProp.ValueKind == JsonValueKind.Null)
                return null;   // Signals "done"

            var name = toolProp.GetString();
            if (string.IsNullOrWhiteSpace(name)) return null;

            // If the LLM already provided inline arguments, capture them to skip Phase 2
            string? inlineArgs = null;
            if (doc.RootElement.TryGetProperty("arguments", out var argsProp) &&
                argsProp.ValueKind == JsonValueKind.Object)
            {
                inlineArgs = argsProp.GetRawText();
            }

            return new Phase1Result(name, inlineArgs);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extracts the first JSON object from a potentially prose-wrapped response.</summary>
    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth == 0) return text[start..(i + 1)]; }
        }
        return null;
    }

    private static ChatOptions BuildChatOptions(
        AgentSettings agentSettings, int numCtx, bool jsonFormat)
    {
        var opts = new ChatOptions
        {
            ModelId = agentSettings.Model,
            Temperature = (float)agentSettings.Temperature,
            MaxOutputTokens = agentSettings.MaxTokens,
        };
        opts.AddOllamaOption(OllamaOption.NumCtx, numCtx);
        if (jsonFormat)
        {
            opts.AdditionalProperties ??= [];
            opts.AdditionalProperties["format"] = "json";
        }
        return opts;
    }

    // ─── CountingEventSink ────────────────────────────────────────────────────

    /// <summary>
    /// Wraps an <see cref="IAgentEventSink"/>, forward all events, and additionally logs
    /// tool executions to <see cref="IEventLogger"/> when tool call pairs are observed.
    /// </summary>
    public sealed class CountingEventSink : IAgentEventSink
    {
        private readonly IAgentEventSink _inner;
        private readonly IEventLogger _logger;
        private readonly string _agentName;
        private readonly string _cycleId;
        private readonly Dictionary<Guid, string> _pendingInputs = new();

        public int ToolCallCount { get; private set; }

        public CountingEventSink(
            IAgentEventSink inner, IEventLogger logger, string agentName, string cycleId)
        {
            _inner = inner;
            _logger = logger;
            _agentName = agentName;
            _cycleId = cycleId;
        }

        public void Publish(AgentEvent evt)
        {
            _inner.Publish(evt);

            switch (evt)
            {
                case ToolCallStartedEvent started:
                    ToolCallCount++;
                    _pendingInputs[started.Id] = started.InputJson;
                    break;

                case ToolCallCompletedEvent completed:
                    var input = _pendingInputs.TryGetValue(completed.Id, out var stored) ? stored : "{}";
                    _pendingInputs.Remove(completed.Id);
                    var elapsedMs = (long)completed.Duration.TotalMilliseconds;
                    _logger.LogToolExecution(completed.ToolName, input, elapsedMs);
                    _logger.LogToolCallJournal(_agentName, _cycleId, ToolCallCount,
                        completed.ToolName, input, completed.Output, elapsedMs);
                    break;
            }
        }
    }
}

