using System.Text.Json;

namespace AgentRunner.Tools;

public class TechnicalIndicatorsTool : Tool
{
    private readonly HttpClient _httpClient;
    private readonly string _marketDataServiceUrl;
    private readonly Dictionary<string, (DateTime Timestamp, object Data)> _cache = new();
    private readonly int _cacheTtlSeconds;

    public override string Name => "technical_indicators";
    public override string Description => "Fetches technical indicator data for a given asset and timeframe";
    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["asset"] = new ToolParameterProperty { Type = "string", Description = "Asset symbol (e.g., BTC, ETH)" },
            ["timeframe"] = new ToolParameterProperty { Type = "string", Description = "Timeframe (1h, 4h, 1d, 1w)", Default = "1d" },
            ["indicators"] = new ToolParameterProperty
            {
                Type = "array",
                Description = "Optional list of specific indicators (RSI, MACD, BB, EMA, Volume)"
            }
        },
        Required = new List<string> { "asset", "timeframe" }
    };

    public TechnicalIndicatorsTool(HttpClient httpClient, string marketDataServiceUrl, int cacheTtlSeconds = 60)
    {
        _httpClient = httpClient;
        _marketDataServiceUrl = marketDataServiceUrl.TrimEnd('/');
        _cacheTtlSeconds = cacheTtlSeconds;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var asset = parameters.GetString("asset");
        if (string.IsNullOrWhiteSpace(asset))
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'asset' parameter" };
        }

        var timeframe = parameters.GetString("timeframe");
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'timeframe' parameter" };
        }

        var indicators = new List<string>();
        if (parameters.TryGetValue("indicators", out var indObj))
        {
            if (indObj is JsonElement { ValueKind: JsonValueKind.Array } indElement)
            {
                indicators = indElement.EnumerateArray()
                    .Select(i => i.GetString() ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
            // If passed as a string (e.g. "all", "RSI,MACD"), parse it  
            else if (indObj is JsonElement { ValueKind: JsonValueKind.String } strElement)
            {
                var raw = strElement.GetString() ?? "";
                if (!string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(raw))
                    indicators = raw.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                // "all" → empty list means fetch everything  
            }
        }

        var cacheKey = $"{asset}_{timeframe}_{string.Join(",", indicators)}";

        if (_cache.TryGetValue(cacheKey, out var cached) &&
            (DateTime.UtcNow - cached.Timestamp).TotalSeconds < _cacheTtlSeconds)
        {
            return new ToolResult { Success = true, Data = cached.Data };
        }

        try
        {
            // Normalise asset: map aliases (XBT → BTC), strip /USD suffix, URL-encode
            var assetUpper = asset.ToUpper().Trim();
            // Strip trailing /USD or USD
            if (assetUpper.EndsWith("/USD")) assetUpper = assetUpper[..^4];
            else if (assetUpper.EndsWith("USD") && assetUpper.Length > 3) assetUpper = assetUpper[..^3];
            // Map common aliases
            assetUpper = assetUpper switch
            {
                "XBT" => "BTC",
                "WBTC" => "BTC",
                "WETH" => "ETH",
                "XETH" => "ETH",
                _ => assetUpper
            };
            if (string.IsNullOrWhiteSpace(assetUpper)) assetUpper = "BTC";
            var symbol = assetUpper + "/USD";
            // Use the symbol as-is in the query string; '/' is permitted in query values
            // and the MarketData API expects the literal 'BTC/USD' format.
            var url = $"{_marketDataServiceUrl}/api/indicators?symbol={symbol}&timeframe={Uri.EscapeDataString(timeframe)}";
            if (indicators.Any())
            {
                url += $"&indicators={string.Join(",", indicators)}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ToolResult { Success = false, Error = $"Market Data Service error: {response.StatusCode}" };
            }

            var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            _cache[cacheKey] = (DateTime.UtcNow, data);

            return new ToolResult { Success = true, Data = data };
        }
        catch (HttpRequestException ex)
        {
            return new ToolResult { Success = false, Error = $"Market data service unavailable: {ex.Message}" };
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
}
