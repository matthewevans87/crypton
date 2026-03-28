using System.Diagnostics;
using System.Text;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AgentRunner.Execution;

/// <summary>
/// Executes a single agent invocation against Ollama via Microsoft.Extensions.AI.
/// Streams tokens to the <see cref="IAgentEventSink"/> and delegates tool calls to
/// the <see cref="IToolProvider"/> pipeline.
/// </summary>
public sealed class LlmAgentExecutor : IAgentExecutor
{
    private readonly IChatClient _chatClient;
    private readonly IToolProvider _toolProvider;
    private readonly AgentRunnerConfig _config;
    private readonly IEventLogger _logger;

    public LlmAgentExecutor(
        IChatClient chatClient,
        IToolProvider toolProvider,
        AgentRunnerConfig config,
        IEventLogger logger)
    {
        _chatClient = chatClient;
        _toolProvider = toolProvider;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AgentOutput> ExecuteAsync(AgentInput input, IAgentEventSink sink, CancellationToken ct = default)
    {
        var settings = _config.GetAgentSettings(input.AgentName);
        var sw = Stopwatch.StartNew();

        // Wrap the sink so tool call iterations can be counted without extra infrastructure.
        var countingSink = new CountingEventSink(sink);

        var options = new ChatOptions
        {
            ModelId = settings.Model,
            Temperature = (float)settings.Temperature,
            Tools = _toolProvider.GetFunctionsForAgent(input.AgentName, input.AvailableTools, countingSink)
                .Cast<AITool>()
                .ToList(),
        };
        (options.AdditionalProperties ??= [])["num_ctx"] = settings.NumCtx ?? _config.Ollama.NumCtx;

        var pipeline = _chatClient.AsBuilder()
            .UseFunctionInvocation(configure: o => { o.MaximumIterationsPerRequest = settings.MaxIterations; })
            .Build();

        var messages = new List<ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, input.SystemPrompt),
            new(Microsoft.Extensions.AI.ChatRole.User, input.UserPrompt),
        };

        _logger.LogPromptSnapshot(input.AgentName, input.CycleId, input.SystemPrompt, input.UserPrompt);

        var responseBuilder = new StringBuilder();

        try
        {
            await foreach (var update in pipeline.GetStreamingResponseAsync(messages, options, ct))
            {
                var token = update.Text;
                if (!string.IsNullOrEmpty(token))
                {
                    responseBuilder.Append(token);
                    countingSink.Publish(new TokenReceivedEvent(token, input.AgentName));
                }
            }

            var output = responseBuilder.ToString();
            sw.Stop();

            _logger.LogInvocationManifest(input.AgentName, input.CycleId, new InvocationManifest(
                Model: settings.Model,
                Temperature: settings.Temperature,
                NumCtx: settings.NumCtx ?? _config.Ollama.NumCtx,
                IterationsUsed: countingSink.ToolCallCount,
                MaxIterations: settings.MaxIterations,
                DurationMs: sw.ElapsedMilliseconds,
                Success: true,
                Error: null));

            return new AgentOutput(Success: true, Output: output, Error: null, Iterations: countingSink.ToolCallCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();

            _logger.LogInvocationManifest(input.AgentName, input.CycleId, new InvocationManifest(
                Model: settings.Model,
                Temperature: settings.Temperature,
                NumCtx: settings.NumCtx ?? _config.Ollama.NumCtx,
                IterationsUsed: countingSink.ToolCallCount,
                MaxIterations: settings.MaxIterations,
                DurationMs: sw.ElapsedMilliseconds,
                Success: false,
                Error: ex.Message));

            return new AgentOutput(Success: false, Output: null, Error: ex.Message, Iterations: countingSink.ToolCallCount);
        }
    }

    // ─── Private helper: counts ToolCallStartedEvents forwarded through the pipeline ────

    private sealed class CountingEventSink : IAgentEventSink
    {
        private readonly IAgentEventSink _inner;
        private int _toolCallCount;

        public CountingEventSink(IAgentEventSink inner) => _inner = inner;

        public int ToolCallCount => _toolCallCount;

        public void Publish(AgentEvent evt)
        {
            if (evt is ToolCallStartedEvent)
                Interlocked.Increment(ref _toolCallCount);

            _inner.Publish(evt);
        }
    }
}
