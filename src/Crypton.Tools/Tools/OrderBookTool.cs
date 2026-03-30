using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches the current order book for a crypto asset.</summary>
[Description("Fetch the current order book (bids and asks) for a crypto asset. Use to assess liquidity, spread, and market depth.")]
public sealed class OrderBookTool : AsyncCommand<OrderBookTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IAnsiConsole _console;

    public OrderBookTool(HttpClient http, string marketDataUrl, IAnsiConsole? console = null)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--asset <ASSET>")]
        [Description("Asset symbol, e.g. BTC or ETH. Quote currency defaults to USD.")]
        public required string Asset { get; init; }

        [CommandOption("--depth <DEPTH>")]
        [Description("Number of price levels to return on each side (1-50).")]
        [DefaultValue(10)]
        public int Depth { get; init; } = 10;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var normalised = NormaliseAsset(settings.Asset);
            var depth = Math.Clamp(settings.Depth, 1, 50);
            var url = $"{_marketDataUrl}/api/orderbook?symbol={Uri.EscapeDataString(normalised)}&depth={depth}";

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
