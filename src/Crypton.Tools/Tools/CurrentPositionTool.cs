using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Retrieves current portfolio state from the Execution Service.</summary>
[Description("Retrieve the current portfolio from the Execution Service: open positions, recent trades, cash balance, and P&L.")]
public sealed class CurrentPositionTool : AsyncCommand<CurrentPositionTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _executionServiceUrl;
    private readonly IAnsiConsole _console;

    public CurrentPositionTool(HttpClient http, string executionServiceUrl, IAnsiConsole? console = null)
    {
        _http = http;
        _executionServiceUrl = executionServiceUrl.TrimEnd('/');
        _console = console ?? AnsiConsole.Console;
    }

    // No parameters — this tool takes no arguments.
    public sealed class Settings : CommandSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.GetAsync($"{_executionServiceUrl}/portfolio/summary", cancellationToken);
            response.EnsureSuccessStatusCode();
            _console.WriteLine(await response.Content.ReadAsStringAsync(cancellationToken));
            return 0;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
