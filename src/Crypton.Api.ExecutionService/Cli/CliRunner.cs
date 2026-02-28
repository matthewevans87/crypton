namespace Crypton.Api.ExecutionService.Cli;

/// <summary>
/// Entry point for CLI mode. Parses verb commands and dispatches to the
/// appropriate handler using the same service layer as service mode.
/// </summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var verb = args[0];

        Console.WriteLine($"CLI mode: {verb}");
        Console.WriteLine("CLI commands are not yet fully implemented.");

        // TODO: Implement CLI handlers for:
        //   status, safe-mode clear, strategy load, promote-to-live, demote-to-paper
        await Task.CompletedTask;
        return 0;
    }
}
