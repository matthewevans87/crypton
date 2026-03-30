using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches live spot prices from the Market Data Service.</summary>
[Description("Fetch live bid/ask/last spot prices for one or more crypto assets. Always prefer this over web_search for current prices.")]
public sealed class GetPriceTool : AsyncCommand<GetPriceTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IAnsiConsole _console;

    public GetPriceTool(HttpClient http, string marketDataUrl, IAnsiConsole? console = null)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--symbols <SYMBOLS>")]
        [Description("Comma-separated asset symbols, e.g. BTC,ETH,SOL. Quote currency defaults to USD.")]
        [DefaultValue("BTC")]
        public string Symbols { get; init; } = "BTC";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var normalised = settings.Symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormaliseAsset)
                .Distinct()
                .ToList();

            if (normalised.Count == 0)
                normalised = ["BTC/USD"];

            var param = Uri.EscapeDataString(string.Join(",", normalised));
            var response = await _http.GetAsync($"{_marketDataUrl}/api/prices?symbols={param}", cancellationToken);
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

    private static string NormaliseAsset(string asset)
    {
        asset = asset.ToUpperInvariant().Trim();
        if (!asset.Contains('/'))
            asset = $"{asset}/USD";
        return asset;
    }
}
