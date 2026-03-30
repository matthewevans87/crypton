using Microsoft.Extensions.AI;

namespace Crypton.Tools;

/// <summary>A named capability that can be invoked by a human via CLI, a program directly, or an AI agent.</summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolDocumentation Documentation { get; }

    /// <summary>
    /// Canonical entry point usable by any caller — agent, program, or CLI wrapper.
    /// Parameters are passed as a string dictionary (matching CLI <c>--key value</c> pairs).
    /// Each tool is responsible for parsing and validating its own parameter values.
    /// Returns the same string payload that <see cref="IAgentTool.AsAIFunction"/> would return.
    /// </summary>
    Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default);
}