using System.Text.Json;

namespace MonitoringDashboard.Services;

public interface IAgentRunnerClient
{
    /// <summary>GET /api/status — current loop state and cycle ID.</summary>
    Task<JsonElement?> GetStatusAsync(CancellationToken ct = default);

    /// <summary>GET /api/cycles?count=n — ordered list of recent cycle metadata.</summary>
    Task<JsonElement?> GetCyclesAsync(int count = 10, CancellationToken ct = default);

    /// <summary>GET /api/cycles/{cycleId} — full artifact dictionary for a single cycle.</summary>
    Task<JsonElement?> GetCycleDetailsAsync(string cycleId, CancellationToken ct = default);

    /// <summary>Generic GET proxy to any AgentRunner REST path; returns raw (statusCode, body).</summary>
    Task<(int StatusCode, string Body)> ProxyGetAsync(string relativePath, CancellationToken ct = default);
}

/// <summary>
/// HTTP polling client that reads AgentRunner REST endpoints.
/// The AgentRunner exposes no SignalR hub; all data is obtained via REST.
/// </summary>
public class AgentRunnerClient : IAgentRunnerClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly ILogger<AgentRunnerClient> _logger;

    public AgentRunnerClient(
        IHttpClientFactory httpClientFactory,
        string baseUrl,
        ILogger<AgentRunnerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task<JsonElement?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.GetAsync($"{_baseUrl}/api/status", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AgentRunner status");
            return null;
        }
    }

    public async Task<JsonElement?> GetCyclesAsync(int count = 10, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.GetAsync($"{_baseUrl}/api/cycles?count={count}", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AgentRunner cycles");
            return null;
        }
    }

    public async Task<JsonElement?> GetCycleDetailsAsync(string cycleId, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.GetAsync($"{_baseUrl}/api/cycles/{Uri.EscapeDataString(cycleId)}", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AgentRunner cycle details for {CycleId}", cycleId);
            return null;
        }
    }

    public async Task<(int StatusCode, string Body)> ProxyGetAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.GetAsync($"{_baseUrl}/{relativePath.TrimStart('/')}", ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to proxy GET {Path} to AgentRunner", relativePath);
            return (503, """{"error":"AgentRunner unavailable"}""");
        }
    }
}
