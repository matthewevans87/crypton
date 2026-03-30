using System.ComponentModel;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches technical indicators (RSI, MACD, BB, EMA, Volume) for a crypto asset.</summary>
public sealed class TechnicalIndicatorsTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "technical_indicators";
    public string Description => "Fetches technical indicator data (RSI, MACD, Bollinger Bands, EMA, Volume) for a given asset and timeframe.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Fetch computed technical indicator data for a specified asset and timeframe. Returns structured signal summaries suitable for direct inclusion in analysis.",
        Parameters:
        [
            new("asset",      "string",  Required: true,  Default: null,       "Base asset symbol, e.g. `\"BTC\"`, `\"ETH\"`, `\"SOL\"`. Do NOT include the quote currency (no `\"BTC/USD\"`)."),
            new("timeframe",  "string",  Required: false, Default: "\"1d\"",    "Candle interval. Accepts: `\"1h\"`, `\"4h\"`, `\"1d\"`, `\"1w\"`. Defaults to `\"1d\"`."),
            new("indicators", "string",  Required: false, Default: "\"\"" ,    "Comma-separated list of indicators to compute: `RSI`, `MACD`, `BB`, `EMA`, `Volume`. Empty string returns all."),
        ],
        Returns:
            "JSON object containing: `symbol`, `timeframe`, `lastUpdated`, `currentPrice`, `high24h`, `low24h`, `volume24h`, " +
            "`rsi` (RSI-14), `macd`/`macdSignal`/`macdHistogram` (MACD 12,26,9), " +
            "`bollingerUpper`/`bollingerMiddle`/`bollingerLower` (BB 20,2), " +
            "and `signal` (`\"overbought\"` when RSI > 70, `\"oversold\"` when RSI < 30, otherwise `\"neutral\"`).",
        Examples:
        [
            "{\"asset\": \"BTC\", \"timeframe\": \"1d\"}",
            "{\"asset\": \"ETH\", \"timeframe\": \"4h\"}",
            "{\"asset\": \"BTC\", \"timeframe\": \"1h\", \"indicators\": \"RSI,MACD\"}",
        ],
        Notes:
        [
            "Use `\"1d\"` for directional thesis work; use `\"4h\"` or `\"1h\"` for entry timing and stop placement.",
            "Call the tool directly rather than relying on indicator values quoted in `research.md`. Research Agent data may be from an earlier timestamp.",
            "Make no more than 8–10 technical indicator calls per session. Fetch multiple timeframes for your primary asset, then stop.",
        ]);

    public TechnicalIndicatorsTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
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

        var timeframe = parameters.TryGetValue("timeframe", out var tf) && !string.IsNullOrWhiteSpace(tf) ? tf : "1d";
        var indicators = parameters.TryGetValue("indicators", out var ind) ? ind : "";

        return ExecuteCoreAsync(asset, timeframe, indicators, ct);
    }

    [Description("Returns technical indicator values for the specified asset and timeframe.")]
    internal async Task<string> ExecuteCoreAsync(
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

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(TechnicalIndicatorsTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<ASSET>")]
            public string Asset { get; init; } = string.Empty;

            [CommandOption("--timeframe <TIMEFRAME>")]
            [DefaultValue("1d")]
            public string Timeframe { get; init; } = "1d";

            [CommandOption("--indicators <INDICATORS>")]
            [DefaultValue("")]
            public string Indicators { get; init; } = "";
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.Asset, settings.Timeframe, settings.Indicators);
            Console.WriteLine(result);
            return 0;
        }
    }
}
