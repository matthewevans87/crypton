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
        if (!parameters.TryGetValue("asset", out var assetObj) || assetObj is not string asset)
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'asset' parameter" };
        }

        if (!parameters.TryGetValue("timeframe", out var tfObj) || tfObj is not string timeframe)
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'timeframe' parameter" };
        }

        var indicators = new List<string>();
        if (parameters.TryGetValue("indicators", out var indObj) && indObj is JsonElement indElement)
        {
            indicators = indElement.EnumerateArray().Select(i => i.GetString() ?? "").ToList();
        }

        var cacheKey = $"{asset}_{timeframe}_{string.Join(",", indicators)}";
        
        if (_cache.TryGetValue(cacheKey, out var cached) && 
            (DateTime.UtcNow - cached.Timestamp).TotalSeconds < _cacheTtlSeconds)
        {
            return new ToolResult { Success = true, Data = cached.Data };
        }

        try
        {
            var symbol = asset.ToUpper() + "/USD";
            var url = $"{_marketDataServiceUrl}/api/indicators?symbol={symbol}&timeframe={timeframe}";
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
