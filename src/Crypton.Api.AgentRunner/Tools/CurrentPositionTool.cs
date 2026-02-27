using System.Net.Http.Json;
using System.Text.Json;

namespace AgentRunner.Tools;

public class CurrentPositionTool : Tool
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly TimeSpan _cacheTtl;
    private DateTime _lastFetch;
    private object? _cachedResult;

    public override string Name => "current_position";
    public override string Description => "Retrieves the current portfolio state from the Execution Service";
    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>()
    };

    public CurrentPositionTool(HttpClient httpClient, string baseUrl, int cacheTtlSeconds = 60)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _cacheTtl = TimeSpan.FromSeconds(cacheTtlSeconds);
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (_cachedResult != null && (DateTime.UtcNow - _lastFetch) < _cacheTtl)
        {
            return new ToolResult { Success = true, Data = _cachedResult };
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/portfolio", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return new ToolResult 
                { 
                    Success = false, 
                    Error = $"Execution Service error: {response.StatusCode} - {errorContent}" 
                };
            }

            var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            
            _cachedResult = data;
            _lastFetch = DateTime.UtcNow;

            return new ToolResult { Success = true, Data = data };
        }
        catch (HttpRequestException ex)
        {
            return new ToolResult { Success = false, Error = $"Connection failed: {ex.Message}" };
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
