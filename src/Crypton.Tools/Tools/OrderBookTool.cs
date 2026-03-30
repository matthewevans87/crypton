using System.ComponentModel;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches the current order book for an asset from the Market Data Service.</summary>
public sealed class OrderBookTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "order_book";
    public string Description => "Fetches the current order book (bids and asks) for a crypto asset. Use to assess liquidity, bid/ask spread, and market depth.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Fetch the current order book (bids and asks) for a crypto asset from Kraken via the Market Data Service. Use to assess liquidity, bid/ask spread, and market microstructure before entering or sizing a position.",
        Parameters:
        [
            new("asset", "string",  Required: true,  Default: null, "Asset symbol, e.g. `\"BTC\"` or `\"ETH\"`. Quote currency defaults to USD."),
            new("depth", "integer", Required: false, Default: "10",  "Number of price levels to return on each side (1–50)."),
        ],
        Returns:
            "JSON object containing `symbol`, `timestamp`, and two arrays `bids` and `asks`. " +
            "Each entry is `[price, quantity]`. Arrays are sorted by price descending (bids) and ascending (asks).",
        Examples:
        [
            "{\"asset\": \"BTC\"}",
            "{\"asset\": \"ETH\", \"depth\": 25}",
        ],
        Notes:
        [
            "A wide bid/ask spread or thin book at the top levels indicates low liquidity — size positions conservatively.",
            "Large order walls (unusually large quantity at a specific price) can act as near-term support or resistance.",
            "Use `depth: 5` for a quick spread check; `depth: 25` or higher for detailed microstructure analysis.",
        ]);

    public OrderBookTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("asset", out var asset) || string.IsNullOrWhiteSpace(asset))
            return Task.FromResult("Error: 'asset' is required.");

        var depth = parameters.TryGetValue("depth", out var depthStr) && int.TryParse(depthStr, out var d) ? d : 10;

        return ExecuteCoreAsync(asset, depth, ct);
    }

    [Description("Returns the current bid/ask order book for a crypto asset.")]
    internal async Task<string> ExecuteCoreAsync(
        [Description("Asset symbol, e.g. BTC or ETH.")] string asset,
        [Description("Number of price levels to return on each side (1–50).")] int depth = 10,
        CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var normalised = NormaliseAsset(asset);
            depth = Math.Clamp(depth, 1, 50);
            var url = $"{_marketDataUrl}/api/orderbook?symbol={Uri.EscapeDataString(normalised)}&depth={depth}";

            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }, cancellationToken);
    }

    private static string NormaliseAsset(string asset)
    {
        asset = asset.ToUpperInvariant().Trim();
        if (!asset.Contains('/'))
            asset = $"{asset}/USD";
        return asset;
    }

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(OrderBookTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<ASSET>")]
            public string Asset { get; init; } = string.Empty;

            [CommandOption("--depth <DEPTH>")]
            [DefaultValue(10)]
            public int Depth { get; init; } = 10;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.Asset, settings.Depth);
            Console.WriteLine(result);
            return 0;
        }
    }
}
