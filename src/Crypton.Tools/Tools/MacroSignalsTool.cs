using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches macro market signals from the Market Data Service.</summary>
[Description("Return macro market signals: trend direction, volatility regime, fear/greed index, and BTC dominance.")]
public sealed class MacroSignalsTool : AsyncCommand<MacroSignalsTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IAnsiConsole _console;

    public MacroSignalsTool(HttpClient http, string marketDataUrl, IAnsiConsole? console = null)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _console = console ?? AnsiConsole.Console;
    }

    // No parameters — this tool takes no arguments.
    public sealed class Settings : CommandSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.GetAsync($"{_marketDataUrl}/api/macro", cancellationToken);
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
