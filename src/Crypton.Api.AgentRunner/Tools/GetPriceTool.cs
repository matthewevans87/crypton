using System.Text.Json;

namespace AgentRunner.Tools;

/// <summary>
/// Fetches live spot prices for one or more assets directly from the Market Data Service.
/// Use this instead of web_search whenever you need a current crypto price.
/// </summary>
public class GetPriceTool : Tool
{
    private static readonly PriceTickerResponseValidator Validator = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _marketDataServiceUrl;
    private readonly Dictionary<string, (DateTime Timestamp, object Data)> _cache = new();
    private readonly int _cacheTtlSeconds;

    public override string Name => "get_price";
    public override string Description =>
        "Fetches the current live spot price(s) for one or more crypto assets from the Market Data Service. " +
        "Always use this tool instead of web_search when you need a current price.";

    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["symbols"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "Comma-separated asset symbols (e.g. \"BTC,ETH,SOL\"). Defaults to BTC if omitted.",
                Default = "BTC"
            }
        },
        Required = new List<string>()
    };

    public GetPriceTool(HttpClient httpClient, string marketDataServiceUrl, int cacheTtlSeconds = 10)
    {
        _httpClient = httpClient;
        _marketDataServiceUrl = marketDataServiceUrl.TrimEnd('/');
        _cacheTtlSeconds = cacheTtlSeconds;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var rawSymbols = parameters.GetString("symbols") ?? "BTC";

        var assetList = rawSymbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormaliseAsset)
            .Distinct()
            .ToList();

        if (assetList.Count == 0)
            assetList = ["BTC/USD"];

        var cacheKey = string.Join(",", assetList);
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            (DateTime.UtcNow - cached.Timestamp).TotalSeconds < _cacheTtlSeconds)
        {
            return new ToolResult { Success = true, Data = cached.Data };
        }

        try
        {
            var symbolsParam = Uri.EscapeDataString(string.Join(",", assetList));
            var url = $"{_marketDataServiceUrl}/api/prices?symbols={symbolsParam}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new ToolResult { Success = false, Error = $"Market Data Service error: {response.StatusCode}" };

            var tickers = await response.Content.ReadFromJsonAsync<List<PriceTickerResponse>>(JsonOptions, cancellationToken)
                ?? [];

            // Validate each ticker; fail fast if any required price is missing
            foreach (var ticker in tickers)
            {
                var validation = Validator.Validate(ticker);
                if (!validation.IsValid)
                {
                    var errors = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
                    return new ToolResult { Success = false, Error = $"Invalid price data for {ticker.Asset}: {errors}" };
                }
            }

            if (tickers.Count == 0)
                return new ToolResult { Success = false, Error = "No price data returned from Market Data Service" };

            // Return a single ticker unwrapped when only one symbol was requested
            object data = tickers.Count == 1 ? tickers[0] : tickers;
            _cache[cacheKey] = (DateTime.UtcNow, data);
            return new ToolResult { Success = true, Data = data };
        }
        catch (HttpRequestException ex)
        {
            return new ToolResult { Success = false, Error = $"Market Data Service unavailable: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new ToolResult { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }

    private static string NormaliseAsset(string asset)
    {
        var upper = asset.ToUpperInvariant();
        if (upper.EndsWith("/USD")) return upper;
        if (upper.EndsWith("USD") && upper.Length > 3) upper = upper[..^3];
        upper = upper switch
        {
            "XBT" => "BTC",
            "WBTC" => "BTC",
            "WETH" => "ETH",
            "XETH" => "ETH",
            _ => upper
        };
        return string.IsNullOrWhiteSpace(upper) ? "BTC/USD" : upper + "/USD";
    }
}
