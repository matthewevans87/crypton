using System.ComponentModel;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches macro market signals (trend, volatility, fear/greed, BTC dominance) from the Market Data Service.</summary>
public sealed class MacroSignalsTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "macro_signals";
    public string Description => "Returns macro market signals: trend direction, volatility regime, fear/greed index, and BTC dominance. Derived from live Kraken data.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Fetch overall crypto market context signals derived from live Kraken data: trend direction, volatility regime, fear/greed index, and BTC dominance. Use to establish the macro backdrop before interpreting asset-specific indicators.",
        Parameters: [],
        Returns:
            "JSON object containing: `trend` (`\"bullish\"`, `\"bearish\"`, or `\"neutral\"`), " +
            "`volatilityRegime` (`\"low\"`, `\"medium\"`, `\"high\"`), " +
            "`fearGreedIndex` (integer 0–100; 0 = extreme fear, 100 = extreme greed), " +
            "`btcDominance` (percentage of total crypto market cap), " +
            "and `timestamp`.",
        Examples: ["{}"],
        Notes:
        [
            "Call this at the start of any analysis step to establish macro context before examining individual assets.",
            "A high fear/greed index (> 75) combined with a bearish trend is a high-risk environment for fresh longs.",
            "BTC dominance rising during a broad sell-off is a risk-off signal; dominance falling in a bull market often precedes an altcoin rotation.",
        ]);

    public MacroSignalsTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
        => ExecuteCoreAsync(ct);

    [Description("Returns overall crypto market context: trend (bullish/bearish/neutral), volatility regime, fear/greed (0–100), and BTC dominance.")]
    internal async Task<string> ExecuteCoreAsync(CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var response = await _http.GetAsync($"{_marketDataUrl}/api/macro", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }, cancellationToken);
    }

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(MacroSignalsTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings { }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync();
            Console.WriteLine(result);
            return 0;
        }
    }
}
