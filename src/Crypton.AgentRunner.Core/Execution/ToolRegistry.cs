using System.Diagnostics;
using System.Text.Json;
using AgentRunner.Abstractions;
using AgentRunner.Domain.Events;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution;

/// <summary>
/// Filters and decorates <see cref="IAgentTool"/> instances for a given agent invocation.
/// Each returned <see cref="AIFunction"/> is wrapped with telemetry that publishes
/// <see cref="ToolCallStartedEvent"/> and <see cref="ToolCallCompletedEvent"/> to the per-call sink.
/// </summary>
public sealed class ToolRegistry : IToolProvider
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AIFunction> GetFunctionsForAgent(
        string agentName,
        IReadOnlyList<string> allowedToolNames,
        IAgentEventSink sink)
    {
        var functions = new List<AIFunction>(allowedToolNames.Count);

        foreach (var name in allowedToolNames)
        {
            if (!_tools.TryGetValue(name, out var tool))
                continue;

            var inner = tool.AsAIFunction();
            functions.Add(new TelemetryAIFunction(inner, agentName, sink));
        }

        return functions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal decorator — wraps any AIFunction with start/complete telemetry
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class TelemetryAIFunction : AIFunction
    {
        private readonly AIFunction _inner;
        private readonly string _agentName;
        private readonly IAgentEventSink _sink;

        public TelemetryAIFunction(AIFunction inner, string agentName, IAgentEventSink sink)
        {
            _inner = inner;
            _agentName = agentName;
            _sink = sink;
        }

        public override string Name => _inner.Name;
        public override string Description => _inner.Description;
        public override JsonElement JsonSchema => _inner.JsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            var inputJson = SerialiseArguments(arguments);

            _sink.Publish(new ToolCallStartedEvent(
                Id: id,
                ToolName: _inner.Name,
                InputJson: inputJson,
                StepName: _agentName));

            var sw = Stopwatch.StartNew();
            var isError = false;
            object? result = null;

            try
            {
                result = await _inner.InvokeAsync(arguments, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                isError = true;
                result = ex.Message;
                throw;
            }
            finally
            {
                sw.Stop();
                _sink.Publish(new ToolCallCompletedEvent(
                    Id: id,
                    ToolName: _inner.Name,
                    Output: result?.ToString() ?? string.Empty,
                    Duration: sw.Elapsed,
                    IsError: isError,
                    StepName: _agentName));
            }
        }

        private static string SerialiseArguments(IEnumerable<KeyValuePair<string, object?>> args)
        {
            try
            {
                var dict = args.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                return JsonSerializer.Serialize(dict);
            }
            catch
            {
                return "{}";
            }
        }
    }
}
