namespace Crypton.Tools;

/// <summary>The result of running a tool command via <see cref="IVirtualTerminal"/>.</summary>
public sealed record ToolResult(int ExitCode, string Output, long ElapsedMs)
{
    public bool IsSuccess => ExitCode == 0;
}
