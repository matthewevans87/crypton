using System.Text.Json;

namespace AgentRunner.Tools;

/// <summary>
/// Fetches the current order book (bid/ask depth) for an asset from the Market Data Service.
/// Use this to assess liquidity, spread, and market depth before sizing a position.
/// </summary>
public class OrderBookTool : Tool
{
    private static readonly OrderBookResponseValidator Validator = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _marketDataServiceUrl;
    private readonly Dictionary<string, (DateTime Timestamp, object Data)> _cache = new();
    private readonly int _cacheTtlSeconds;

    public override string Name => "order_book";
    public override string Description =>
        "Fetches the current order book (bids and asks) for a crypto asset from the Market Data Service. " +
        "Reveals liquidity, bid/ask spread, and market depth to inform entry and exit sizing.";

    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["asset"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "Asset symbol (e.g. BTC, ETH)"
            },
            ["depth"] = new ToolParameterProperty
            {
                Type = "integer",
                Description = "Number of price levels to return on each side (default 10, max 50)",
                Default = "10"
            }
        },
        Required = new List<string> { "asset" }
    };

    public OrderBookTool(HttpClient httpClient, string marketDataServiceUrl, int cacheTtlSeconds = 10)
    {
        _httpClient = httpClient;
        _marketDataServiceUrl = marketDataServiceUrl.TrimEnd('/');
        _cacheTtlSeconds = cacheTtlSeconds;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var asset = parameters.GetString("asset");
        if (string.IsNullOrWhiteSpace(asset))
            return new ToolResult { Success = false, Error = "Missing or invalid 'asset' parameter" };

        var symbol = NormaliseAsset(asset);

        var depth = 10;
        if (parameters.TryGetValue("depth", out var depthObj))
        {
            if (depthObj is JsonElement { ValueKind: JsonValueKind.Number } el)
                depth = Math.Clamp(el.GetInt32(), 1, 50);
        }

        var cacheKey = $"{symbol}_{depth}";
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            (DateTime.UtcNow - cached.Timestamp).TotalSeconds < _cacheTtlSeconds)
        {
            return new ToolResult { Success = true, Data = cached.Data };
        }

        try
        {
            var encodedSymbol = Uri.EscapeDataString(symbol);
            var url = $"{_marketDataServiceUrl}/api/orderbook/{encodedSymbol}?depth={depth}";

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new ToolResult { Success = false, Error = $"Market Data Service error: {response.StatusCode}" };

            var data = await response.Content.ReadFromJsonAsync<OrderBookResponse>(JsonOptions, cancellationToken)
                ?? new OrderBookResponse();

            var validation = Validator.Validate(data);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
                return new ToolResult { Success = false, Error = $"Market Data Service response validation failed: {errors}" };
            }

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
        var upper = asset.ToUpperInvariant().Trim();
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
