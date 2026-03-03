using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

namespace MonitoringDashboard.Services;

public interface IAgentRunnerClient
{
    // SignalR events
    event EventHandler<JsonElement>? OnStatusUpdate;
    event EventHandler<JsonElement>? OnStepStarted;
    event EventHandler<JsonElement>? OnStepCompleted;
    event EventHandler<JsonElement>? OnTokenReceived;
    event EventHandler<JsonElement>? OnToolCallStarted;
    event EventHandler<JsonElement>? OnToolCallCompleted;

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    // REST helpers
    Task<JsonElement?> GetStatusAsync(CancellationToken ct = default);
    Task<JsonElement?> GetCyclesAsync(int count = 10, CancellationToken ct = default);
    Task<JsonElement?> GetCycleDetailsAsync(string cycleId, CancellationToken ct = default);
    Task<(int StatusCode, string Body)> ProxyGetAsync(string relativePath, CancellationToken ct = default);
}

/// <summary>
/// Connects to the AgentRunner SignalR hub (<c>/hubs/agent-runner</c>) for real-time
/// streaming, and proxies REST calls to the AgentRunner REST API.
/// Follows the same pattern as <see cref="ExecutionServiceClient"/>.
/// </summary>
public class AgentRunnerClient : IAgentRunnerClient, IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _baseUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgentRunnerClient> _logger;
    private bool _isConnected;

    public event EventHandler<JsonElement>? OnStatusUpdate;
    public event EventHandler<JsonElement>? OnStepStarted;
    public event EventHandler<JsonElement>? OnStepCompleted;
    public event EventHandler<JsonElement>? OnTokenReceived;
    public event EventHandler<JsonElement>? OnToolCallStarted;
    public event EventHandler<JsonElement>? OnToolCallCompleted;

    public bool IsConnected => _isConnected;

    public AgentRunnerClient(
        IHttpClientFactory httpClientFactory,
        string baseUrl,
        ILogger<AgentRunnerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
    }

    // ── SignalR ──────────────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/hubs/agent-runner")
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            // Status group messages
            _connection.On<JsonElement>("StatusUpdate", payload => OnStatusUpdate?.Invoke(this, payload));
            _connection.On<JsonElement>("StateChanged", payload => OnStatusUpdate?.Invoke(this, payload));
            _connection.On<JsonElement>("CycleCompleted", payload => OnStatusUpdate?.Invoke(this, payload));
            _connection.On<JsonElement>("ErrorOccurred", payload => OnStatusUpdate?.Invoke(this, payload));

            // Steps group messages
            _connection.On<JsonElement>("StepStarted", payload => OnStepStarted?.Invoke(this, payload));
            _connection.On<JsonElement>("StepCompleted", payload => OnStepCompleted?.Invoke(this, payload));

            // Tokens group messages
            _connection.On<JsonElement>("TokenReceived", payload => OnTokenReceived?.Invoke(this, payload));

            // ToolCalls group messages
            _connection.On<JsonElement>("ToolCallStarted", payload => OnToolCallStarted?.Invoke(this, payload));
            _connection.On<JsonElement>("ToolCallCompleted", payload => OnToolCallCompleted?.Invoke(this, payload));

            _connection.Closed += async error =>
            {
                _isConnected = false;
                _logger.LogWarning(error, "AgentRunner hub connection closed");
                await Task.CompletedTask;
            };

            _connection.Reconnected += async _ =>
            {
                _isConnected = true;
                _logger.LogInformation("Reconnected to AgentRunner hub; re-subscribing to groups");
                await SubscribeToGroupsAsync();
            };

            _connection.Reconnecting += async error =>
            {
                _logger.LogWarning(error, "Reconnecting to AgentRunner hub...");
                await Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);
            await SubscribeToGroupsAsync();
            _isConnected = true;
            _logger.LogInformation("Connected to AgentRunner SignalR hub at {Url}", _baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to AgentRunner hub");
            _isConnected = false;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
            _isConnected = false;
        }
    }

    private async Task SubscribeToGroupsAsync()
    {
        if (_connection is null) return;
        await _connection.InvokeAsync("SubscribeToStatus");
        await _connection.InvokeAsync("SubscribeToSteps");
        await _connection.InvokeAsync("SubscribeToTokens");
        await _connection.InvokeAsync("SubscribeToToolCalls");
    }

    // ── REST helpers ─────────────────────────────────────────────────────────

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

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
