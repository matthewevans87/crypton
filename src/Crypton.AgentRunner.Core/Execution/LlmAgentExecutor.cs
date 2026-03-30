using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace AgentRunner.Execution;

/// <summary>
/// Drives a single LLM agent invocation: builds <see cref="ChatOptions"/>, manages the
/// think → tool-call → respond agentic loop and streams <see cref="AgentEvent"/>s to the
/// per-call <see cref="IAgentEventSink"/>.
/// </summary>
public sealed class LlmAgentExecutor : IAgentExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IChatClient _chatClient;
    private readonly IToolProvider _toolProvider;
    private readonly IEventLogger _logger;
    private readonly AgentRunnerConfig _config;

    public LlmAgentExecutor(
        IChatClient chatClient,
        IToolProvider toolProvider,
        IEventLogger logger,
        AgentRunnerConfig config)
    {
        _chatClient = chatClient;
        _toolProvider = toolProvider;
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc/>
    public async Task<AgentOutput> ExecuteAsync(
        AgentInput input,
        IAgentEventSink sink,
        CancellationToken ct = default)
    {
        var agentSettings = _config.GetAgentSettings(input.AgentName);
        var numCtx = agentSettings.NumCtx ?? _config.Ollama.NumCtx;

        var countingSink = new CountingEventSink(sink, _logger, input.AgentName, input.CycleId);

        var functions = _toolProvider.GetFunctionsForAgent(input.AgentName, input.AvailableTools, countingSink);
        var functionsByName = functions.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, input.SystemPrompt),
            new(ChatRole.User, input.UserPrompt),
        };

        var chatOptions = new ChatOptions
        {
            ModelId = agentSettings.Model,
            Temperature = (float)agentSettings.Temperature,
            MaxOutputTokens = agentSettings.MaxTokens,
            Tools = functions.Cast<AITool>().ToList(),
            ToolMode = ChatToolMode.Auto,
        };
        chatOptions.AddOllamaOption(OllamaOption.NumCtx, numCtx);

        var diagnostics = Stopwatch.StartNew();
        var iterations = 0;
        string? lastTextOutput = null;
        string? errorMessage = null;
        var success = false;

        try
        {
            while (iterations < agentSettings.MaxIterations && !ct.IsCancellationRequested)
            {
                iterations++;

                var textBuilder = new StringBuilder();
                var responseContents = new List<AIContent>();

                await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions, ct))
                {
                    if (update.Text is { Length: > 0 } token)
                    {
                        textBuilder.Append(token);
                        countingSink.Publish(new TokenReceivedEvent(token, input.AgentName));
                    }

                    foreach (var content in update.Contents)
                        responseContents.Add(content);
                }

                messages.Add(new ChatMessage(ChatRole.Assistant, responseContents));

                if (textBuilder.Length > 0)
                    lastTextOutput = textBuilder.ToString();

                var functionCalls = responseContents.OfType<FunctionCallContent>().ToList();
                if (functionCalls.Count == 0)
                {
                    success = true;
                    break;
                }

                // Invoke each requested function and accumulate results.
                var resultContents = new List<AIContent>(functionCalls.Count);

                foreach (var call in functionCalls)
                {
                    ct.ThrowIfCancellationRequested();

                    var eventId = Guid.NewGuid();
                    var inputJson = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments, JsonOptions)
                        : "{}";

                    countingSink.Publish(new ToolCallStartedEvent(eventId, call.Name, inputJson, input.AgentName));

                    var sw = Stopwatch.StartNew();
                    string resultStr = string.Empty;
                    var isError = false;

                    if (functionsByName.TryGetValue(call.Name, out var function))
                    {
                        try
                        {
                            var args = new AIFunctionArguments();
                            if (call.Arguments is not null)
                                foreach (var (k, v) in call.Arguments)
                                    args.Add(k, v!);

                            var result = await function.InvokeAsync(args, ct);
                            resultStr = result?.ToString() ?? string.Empty;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            resultStr = ex.Message;
                            isError = true;
                        }
                    }
                    else
                    {
                        resultStr = $"Error: tool '{call.Name}' is not available.";
                        isError = true;
                    }

                    sw.Stop();
                    countingSink.Publish(new ToolCallCompletedEvent(
                        eventId, call.Name, resultStr, sw.Elapsed, isError, input.AgentName));

                    resultContents.Add(new FunctionResultContent(call.CallId, resultStr));
                }

                messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
            }

            if (!success && errorMessage is null)
                errorMessage = $"Max iterations ({agentSettings.MaxIterations}) exceeded without producing a final response.";
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

    // ─── Nested types ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps the per-call <see cref="IAgentEventSink"/> to count tool invocations and log them
    /// to the structured <see cref="IEventLogger"/> journal.
    /// <para>
    /// Correlates <see cref="ToolCallStartedEvent"/> and <see cref="ToolCallCompletedEvent"/> by
    /// <see cref="ToolCallStartedEvent.Id"/> to capture the input JSON at Start time and write it
    /// alongside the output in <see cref="IEventLogger.LogToolCallJournal"/>.
    /// </para>
    /// </summary>
    public sealed class CountingEventSink : IAgentEventSink
    {
        private readonly IAgentEventSink _inner;
        private readonly IEventLogger _logger;
        private readonly string _agentName;
        private readonly string _cycleId;
        private readonly Dictionary<Guid, string> _pendingInputsByCallId = new();
        private int _toolCallCount;

        public int ToolCallCount => _toolCallCount;

        public CountingEventSink(
            IAgentEventSink inner,
            IEventLogger logger,
            string agentName,
            string cycleId)
        {
            _inner = inner;
            _logger = logger;
            _agentName = agentName;
            _cycleId = cycleId;
        }

        public void Publish(AgentEvent evt)
        {
            switch (evt)
            {
                case ToolCallStartedEvent started:
                    _pendingInputsByCallId[started.Id] = started.InputJson;
                    _toolCallCount++;
                    break;

                case ToolCallCompletedEvent completed:
                    var inputJson = _pendingInputsByCallId.TryGetValue(completed.Id, out var cached)
                        ? cached
                        : "{}";
                    _pendingInputsByCallId.Remove(completed.Id);

                    var durationMs = (long)completed.Duration.TotalMilliseconds;
                    _logger.LogToolExecution(completed.ToolName, inputJson, durationMs);
                    _logger.LogToolCallJournal(
                        _agentName, _cycleId, _toolCallCount,
                        completed.ToolName, inputJson, completed.Output, durationMs);
                    break;
            }

            _inner.Publish(evt);
        }
    }
}
