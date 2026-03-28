using System.ComponentModel;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Fetches technical indicators (RSI, MACD, BB, EMA, Volume) for a crypto asset.</summary>
public sealed class TechnicalIndicatorsTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "technical_indicators";
    public string Description => "Fetches technical indicator data (RSI, MACD, Bollinger Bands, EMA, Volume) for a given asset and timeframe.";

    public TechnicalIndicatorsTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Returns technical indicator values for the specified asset and timeframe.")]
    private async Task<string> ExecuteAsync(
        [Description("Asset symbol, e.g. BTC or ETH.")] string asset,
        [Description("Timeframe: 1h, 4h, 1d, or 1w.")] string timeframe = "1d",
        [Description("Comma-separated list of indicators (RSI, MACD, BB, EMA, Volume), or empty for all.")] string indicators = "",
        CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var normalised = NormaliseAsset(asset);
            var url = $"{_marketDataUrl}/api/indicators?asset={Uri.EscapeDataString(normalised)}&timeframe={Uri.EscapeDataString(timeframe)}";

            if (!string.IsNullOrWhiteSpace(indicators))
                url += $"&indicators={Uri.EscapeDataString(indicators)}";

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
