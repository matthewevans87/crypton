using System.ComponentModel;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Fetches macro market signals (trend, volatility, fear/greed, BTC dominance) from the Market Data Service.</summary>
public sealed class MacroSignalsTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "macro_signals";
    public string Description => "Returns macro market signals: trend direction, volatility regime, fear/greed index, and BTC dominance. Derived from live Kraken data.";

    public MacroSignalsTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Returns overall crypto market context: trend (bullish/bearish/neutral), volatility regime, fear/greed (0–100), and BTC dominance.")]
    private async Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var response = await _http.GetAsync($"{_marketDataUrl}/api/macro", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }, cancellationToken);
    }
}
