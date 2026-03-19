using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

namespace MonitoringDashboard.Services;

public interface IExecutionServiceClient
{
    event EventHandler<JsonElement>? OnStatusUpdate;
    event EventHandler<JsonElement>? OnPositionUpdate;
    event EventHandler<string>? OnPositionClosed;
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    Task<(int StatusCode, string Body)> GetPositionsAsync(CancellationToken ct = default);
    Task<(int StatusCode, string Body)> GetTradesAsync(CancellationToken ct = default);
    Task<(int StatusCode, string Body)> GetStrategyAsync(CancellationToken ct = default);
    Task<(int StatusCode, string Body)> GetRawStatusAsync(CancellationToken ct = default);
}

/// <summary>
/// Connects to the ExecutionService SignalR hub (<c>/hubs/execution</c>) and proxies REST calls to
/// the ExecutionService REST API.  Mirrors the <see cref="MarketDataServiceClient"/> pattern.
/// </summary>
public class ExecutionServiceClient : IExecutionServiceClient, IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExecutionServiceClient> _logger;
    private bool _isConnected;

    public event EventHandler<JsonElement>? OnStatusUpdate;
    public event EventHandler<JsonElement>? OnPositionUpdate;
    public event EventHandler<string>? OnPositionClosed;

    public bool IsConnected => _isConnected;

    public ExecutionServiceClient(
        string baseUrl,
        string apiKey,
        HttpClient httpClient,
        ILogger<ExecutionServiceClient> logger)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _httpClient = httpClient;
        _logger = logger;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_baseUrl}/{path.TrimStart('/')}");

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Add("X-Api-Key", _apiKey);
        }

        return request;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/hubs/execution")
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            _connection.On<JsonElement>("StatusUpdate", payload =>
            {
                _logger.LogDebug("Received ExecutionService StatusUpdate");
                OnStatusUpdate?.Invoke(this, payload);
            });

            _connection.On<JsonElement>("PositionUpdate", payload =>
            {
                _logger.LogDebug("Received ExecutionService PositionUpdate");
                OnPositionUpdate?.Invoke(this, payload);
            });

            _connection.On<JsonElement>("PositionClosed", payload =>
            {
                var positionId = payload.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                _logger.LogDebug("Received ExecutionService PositionClosed: {PositionId}", positionId);
                OnPositionClosed?.Invoke(this, positionId);
            });

            _connection.Closed += async error =>
            {
                _isConnected = false;
                _logger.LogWarning(error, "ExecutionService hub connection closed");
                await Task.CompletedTask;
            };

            _connection.Reconnected += async _ =>
            {
                _isConnected = true;
                _logger.LogInformation("Reconnected to ExecutionService hub; re-subscribing to groups");
                await SubscribeToGroupsAsync();
            };

            _connection.Reconnecting += async error =>
            {
                _logger.LogWarning(error, "Reconnecting to ExecutionService hub...");
                await Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);
            await SubscribeToGroupsAsync();
            _isConnected = true;
            _logger.LogInformation("Connected to ExecutionService SignalR hub at {Url}", _baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to ExecutionService hub");
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
        await _connection.InvokeAsync("SubscribeToPositions");
    }

    // -----------------------------------------------------------------------
    // REST proxy helpers
    // -----------------------------------------------------------------------

    public async Task<(int StatusCode, string Body)> GetPositionsAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "positions");
            var resp = await _httpClient.SendAsync(request, ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get positions from ExecutionService");
            return (503, """{"error":"ExecutionService unavailable"}""");
        }
    }

    public async Task<(int StatusCode, string Body)> GetTradesAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "trades");
            var resp = await _httpClient.SendAsync(request, ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get trades from ExecutionService");
            return (503, """{"error":"ExecutionService unavailable"}""");
        }
    }

    public async Task<(int StatusCode, string Body)> GetStrategyAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "strategy");
            var resp = await _httpClient.SendAsync(request, ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get strategy from ExecutionService");
            return (503, """{"error":"ExecutionService unavailable"}""");
        }
    }

    public async Task<(int StatusCode, string Body)> GetRawStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "api/status");
            var resp = await _httpClient.SendAsync(request, ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get status from ExecutionService");
            return (503, """{"error":"ExecutionService unavailable"}""");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
