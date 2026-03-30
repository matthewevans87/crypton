using AgentRunner.Abstractions;
using Crypton.Tools;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution;

/// <summary>
/// Bridges <see cref="IAgentTool"/> implementations to the <see cref="IToolProvider"/>
/// contract consumed by <see cref="LlmAgentExecutor"/>.
/// </summary>
internal sealed class ToolRegistry : IToolProvider
{
    private readonly IReadOnlyDictionary<Type, IAgentTool> _toolsByType;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _toolsByType = tools.ToDictionary(t => t.GetType());
    }

    public IReadOnlyList<AIFunction> GetFunctionsForAgent(
        string agentName,
        IReadOnlyList<Type> allowedToolTypes,
        IAgentEventSink sink)
    {
        return allowedToolTypes
            .Where(_toolsByType.ContainsKey)
            .Select(t => _toolsByType[t].AsAIFunction())
            .ToList();
    }

    public IReadOnlyList<(string Name, ToolDocumentation Documentation)> GetDocumentationFor(
        IReadOnlyList<Type> allowedToolTypes)
    {
        return allowedToolTypes
            .Where(_toolsByType.ContainsKey)
            .Select(t => (_toolsByType[t].Name, _toolsByType[t].Documentation))
            .ToList();
    }
}
