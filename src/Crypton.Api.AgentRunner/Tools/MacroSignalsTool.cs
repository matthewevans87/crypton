using System.Text.Json;

namespace AgentRunner.Tools;

/// <summary>
/// Fetches macro market signals from the Market Data Service: trend direction, volatility regime,
/// fear/greed proxy, and BTC dominance. All values are derived from live Kraken ticker data.
/// </summary>
public class MacroSignalsTool : Tool
{
    private static readonly MacroSignalsResponseValidator Validator = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly string _marketDataServiceUrl;
    private (DateTime Timestamp, MacroSignalsResponse? Data) _cache;
    private readonly int _cacheTtlSeconds;

    public override string Name => "macro_signals";
    public override string Description =>
        "Returns macro market signals derived from live exchange data: trend (bullish/bearish/neutral), " +
        "volatility regime (low/normal/high), fear/greed index (0–100), market sentiment, and BTC dominance. " +
        "Use this to assess overall market context before making trading decisions.";

    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>(),
        Required = new List<string>()
    };

    public MacroSignalsTool(HttpClient httpClient, string marketDataServiceUrl, int cacheTtlSeconds = 60)
    {
        _httpClient = httpClient;
        _marketDataServiceUrl = marketDataServiceUrl.TrimEnd('/');
        _cacheTtlSeconds = cacheTtlSeconds;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (_cache.Data != null &&
            (DateTime.UtcNow - _cache.Timestamp).TotalSeconds < _cacheTtlSeconds)
        {
            return new ToolResult { Success = true, Data = _cache.Data };
        }

        try
        {
            var url = $"{_marketDataServiceUrl}/api/macro";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new ToolResult { Success = false, Error = $"Market Data Service error: {response.StatusCode}" };

            var data = await response.Content.ReadFromJsonAsync<MacroSignalsResponse>(JsonOptions, cancellationToken)
                ?? new MacroSignalsResponse();

            var validation = Validator.Validate(data);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
                return new ToolResult { Success = false, Error = $"Market Data Service response validation failed: {errors}" };
            }

            _cache = (DateTime.UtcNow, data);
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
}
