using System.ComponentModel;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Fetches the current order book for an asset from the Market Data Service.</summary>
public sealed class OrderBookTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "order_book";
    public string Description => "Fetches the current order book (bids and asks) for a crypto asset. Use to assess liquidity, bid/ask spread, and market depth.";

    public OrderBookTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Returns the current bid/ask order book for a crypto asset.")]
    private async Task<string> ExecuteAsync(
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
}
