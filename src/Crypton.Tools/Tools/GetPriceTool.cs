using System.ComponentModel;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches live spot prices directly from the Market Data Service.</summary>
public sealed class GetPriceTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _marketDataUrl;
    private readonly IToolExecutor _executor;

    public string Name => "get_price";
    public string Description => "Fetches the current live spot price(s) for one or more crypto assets. Always use this instead of web_search when you need a current price.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Fetch the current live bid/ask/last spot price(s) for one or more crypto assets directly from the Market Data Service (Kraken). This is the authoritative, lowest-latency price source — always prefer it over `web_search` whenever you need a current price.",
        Parameters:
        [
            new("symbols", "string", Required: false, Default: "\"BTC\"", "Comma-separated asset symbols, e.g. `\"BTC,ETH,SOL\"`. Quote currency defaults to USD if omitted (e.g. `\"BTC\"` → `\"BTC/USD\"`). Full pairs like `\"BTC/USD\"` are also accepted."),
        ],
        Returns: "JSON response from the Market Data Service containing current bid, ask, and last-trade prices for each requested symbol.",
        Examples:
        [
            "{\"symbols\": \"BTC\"}",
            "{\"symbols\": \"BTC,ETH,SOL\"}",
            "{\"symbols\": \"BTC/USD,ETH/USD\"}",
        ],
        Notes:
        [
            "Always use this tool instead of `web_search` when you need the current price of a crypto asset.",
            "Multiple assets can be fetched in a single call — prefer this over calling the tool once per asset.",
            "Prices are live Kraken spot data; latency is typically under 1 second.",
        ]);

    public GetPriceTool(HttpClient http, string marketDataUrl, IToolExecutor executor)
    {
        _http = http;
        _marketDataUrl = marketDataUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        var symbols = parameters.TryGetValue("symbols", out var s) && !string.IsNullOrWhiteSpace(s) ? s : "BTC";
        return ExecuteCoreAsync(symbols, ct);
    }

    [Description("Returns live bid/ask/last prices for one or more crypto assets.")]
    internal async Task<string> ExecuteCoreAsync(
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

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(GetPriceTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandOption("--symbols <SYMBOLS>")]
            [DefaultValue("BTC")]
            public string Symbols { get; init; } = "BTC";
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.Symbols);
            Console.WriteLine(result);
            return 0;
        }
    }
}
