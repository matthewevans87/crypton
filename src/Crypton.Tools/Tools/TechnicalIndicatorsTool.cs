using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches technical indicators (RSI, MACD, BB, EMA, Volume) for a crypto asset.</summary>
[Description("Fetch computed technical indicators (RSI, MACD, Bollinger Bands, EMA, Volume) for an asset and timeframe.")]
public sealed class TechnicalIndicatorsTool : AsyncCommand<TechnicalIndicatorsTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IAnsiConsole _console;

    public TechnicalIndicatorsTool(HttpClient http, string marketDataUrl, IAnsiConsole? console = null)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--asset <ASSET>")]
        [Description("Asset symbol, e.g. BTC or ETH. Do not include the quote currency.")]
        public required string Asset { get; init; }

        [CommandOption("--timeframe <TIMEFRAME>")]
        [Description("Candle interval: 1h, 4h, 1d, or 1w.")]
        [DefaultValue("1d")]
        public string Timeframe { get; init; } = "1d";

        [CommandOption("--indicators <INDICATORS>")]
        [Description("Comma-separated indicator list: RSI, MACD, BB, EMA, Volume. Empty returns all.")]
        [DefaultValue("")]
        public string Indicators { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var normalised = NormaliseAsset(settings.Asset);
            var url = $"{_marketDataUrl}/api/indicators?asset={Uri.EscapeDataString(normalised)}&timeframe={Uri.EscapeDataString(settings.Timeframe)}";

            if (!string.IsNullOrWhiteSpace(settings.Indicators))
                url += $"&indicators={Uri.EscapeDataString(settings.Indicators)}";

            var response = await _http.GetAsync(url, cancellationToken);
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
