using System.ComponentModel;
using System.Net.Http.Json;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Fetches live spot prices directly from the Market Data Service.</summary>
public sealed class GetPriceTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "get_price";
    public string Description => "Fetches the current live spot price(s) for one or more crypto assets. Always use this instead of web_search when you need a current price.";

    public GetPriceTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Returns live bid/ask/last prices for one or more crypto assets.")]
    private async Task<string> ExecuteAsync(
        [Description("Comma-separated asset symbols, e.g. BTC,ETH,SOL")]
        string symbols = "BTC",
        CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var normalised = symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormaliseAsset)
                .Distinct()
                .ToList();

            if (normalised.Count == 0)
                normalised = ["BTC/USD"];

            var param = Uri.EscapeDataString(string.Join(",", normalised));
            var response = await _http.GetAsync($"{_marketDataUrl}/api/prices?symbols={param}", ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            return body;
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
