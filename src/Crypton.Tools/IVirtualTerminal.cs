namespace Crypton.Tools;

/// <summary>
/// Allows in-process invocation of a registered tool command.
/// The tool runs as if executed in a real terminal: output is captured as a string,
/// and the exit code indicates success (0) or failure (non-zero).
/// </summary>
public interface IVirtualTerminal
{
    /// <param name="toolName">The registered tool name (e.g. <c>"web_search"</c>).</param>
    /// <param name="jsonArgs">JSON object whose keys map to the tool's command options,
    /// e.g. <c>{"query": "bitcoin news", "count": 5}</c>.</param>
    Task<ToolResult> RunAsync(string toolName, string jsonArgs, CancellationToken ct = default);
}
