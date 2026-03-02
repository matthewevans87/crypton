using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Orders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Exchange;

/// <summary>
/// Connects to Kraken's authenticated WebSocket API v2 (<c>wss://ws-auth.kraken.com/v2</c>)
/// and subscribes to the <c>executions</c> channel. Fill events are forwarded to
/// <see cref="OrderRouter.ApplyFillByExchangeOrderIdAsync"/> so live orders are reconciled
/// as fast as possible without polling.
///
/// Lifecycle:
/// <list type="bullet">
///   <item>Only activates (opens connection) when <see cref="IOperationModeService.CurrentMode"/> is <c>"live"</c>.</item>
///   <item>Automatically reconnects with exponential back-off on drop.</item>
///   <item>Re-fetches the WebSocket token on every (re)connect; also proactively every 12 minutes
///       before the 15-minute expiry window.</item>
///   <item>Gracefully disconnects on service shutdown or mode change back to <c>"paper"</c>.</item>
/// </list>
/// </summary>
public sealed class KrakenWsExecutionAdapter : IHostedService, IAsyncDisposable
{
    // Auth WS endpoint for private channels (executions, balances, etc.)
    private const string DefaultAuthWsUrl = "wss://ws-auth.kraken.com/v2";

    // Token lifetime is 15 min; refresh proactively with 3-minute buffer.
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(12);

    private readonly KrakenRestAdapter _rest;
    private readonly OrderRouter _router;
    private readonly IOperationModeService _modeService;
    private readonly ILogger<KrakenWsExecutionAdapter> _logger;
    private readonly string _authWsUrl;
    private readonly Func<ClientWebSocket> _webSocketFactory;

    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public KrakenWsExecutionAdapter(
        KrakenRestAdapter rest,
        OrderRouter router,
        IOperationModeService modeService,
        ILogger<KrakenWsExecutionAdapter> logger,
        string? authWsUrl = null,
        Func<ClientWebSocket>? webSocketFactory = null)
    {
        _rest = rest;
        _router = router;
        _modeService = modeService;
        _logger = logger;
        _authWsUrl = authWsUrl ?? DefaultAuthWsUrl;
        _webSocketFactory = webSocketFactory ?? (() => new ClientWebSocket());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IHostedService
    // ─────────────────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_runTask is not null)
            {
                try { await _runTask.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "KrakenWsExecutionAdapter run task threw on shutdown.");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Dispose();
        if (_runTask is not null)
        {
            try { await _runTask; }
            catch { /* swallow */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Main loop
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        int backoffSeconds = 2;
        const int maxBackoffSeconds = 60;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait for live mode before opening the private WS channel.
            if (_modeService.CurrentMode != "live")
            {
                _logger.LogDebug("KrakenWsExecutionAdapter: mode={Mode}, waiting for live…",
                    _modeService.CurrentMode);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                _logger.LogInformation("KrakenWsExecutionAdapter: fetching WS token for live connection.");
                var token = await _rest.GetWebSocketsTokenAsync(stoppingToken);

                _logger.LogInformation("KrakenWsExecutionAdapter: connecting to {Url}", _authWsUrl);
                await ConnectAndConsumeAsync(token, stoppingToken);

                // Successful clean exit (e.g. mode changed back to paper).
                backoffSeconds = 2;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "KrakenWsExecutionAdapter: connection lost, reconnecting in {Secs}s.", backoffSeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken); }
                catch (OperationCanceledException) { return; }

                backoffSeconds = Math.Min(backoffSeconds * 2, maxBackoffSeconds);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WebSocket session
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ConnectAndConsumeAsync(string wsToken, CancellationToken stoppingToken)
    {
        using var ws = _webSocketFactory();
        await ws.ConnectAsync(new Uri(_authWsUrl), stoppingToken);

        _logger.LogInformation("KrakenWsExecutionAdapter: authenticated WS connected.");

        // Subscribe to the executions channel.
        var subscribeMsg = JsonSerializer.Serialize(new
        {
            method = "subscribe",
            @params = new
            {
                channel = "executions",
                token = wsToken,
                snap_orders = true,
                snap_trades = false
            }
        });
        await ws.SendAsync(
            Encoding.UTF8.GetBytes(subscribeMsg),
            WebSocketMessageType.Text,
            endOfMessage: true,
            stoppingToken);

        // Schedule a proactive token refresh.
        using var tokenRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var tokenAge = DateTimeOffset.UtcNow;

        // Read loop
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();

        while (!stoppingToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            // Proactive token refresh: reconnect to pick up a fresh token.
            if (DateTimeOffset.UtcNow - tokenAge >= TokenRefreshInterval)
            {
                _logger.LogInformation(
                    "KrakenWsExecutionAdapter: token nearing expiry, reconnecting for refresh.");
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "token_refresh", stoppingToken);
                return; // Caller's loop will reconnect with a new token.
            }

            // Mode guard: disconnect if switched back to paper.
            if (_modeService.CurrentMode != "live")
            {
                _logger.LogInformation(
                    "KrakenWsExecutionAdapter: mode changed to paper, closing WS.");
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "paper_mode", stoppingToken);
                return;
            }

            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("KrakenWsExecutionAdapter: server closed the WS.");
                return;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (!result.EndOfMessage) continue;

            var message = sb.ToString();
            sb.Clear();

            HandleMessage(message);
        }
    }

    /// <summary>
    /// Parses a raw WebSocket message from the Kraken private executions channel
    /// and forwards fill events to <see cref="OrderRouter"/>.
    /// Exposed as <c>internal</c> so unit tests can exercise message parsing without
    /// requiring a live WebSocket connection.
    /// </summary>
    internal void HandleMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return;

            var channel = node["channel"]?.GetValue<string>();
            if (channel != "executions") return;

            var type = node["type"]?.GetValue<string>(); // "snapshot" | "update"

            var data = node["data"]?.AsArray();
            if (data is null) return;

            // Fire-and-forget each fill event; exceptions are caught inside.
            foreach (var item in data)
            {
                if (item is null) continue;

                var execType = item["exec_type"]?.GetValue<string>();
                var orderStatus = item["order_status"]?.GetValue<string>();

                // Only process actual trade executions; skip "new", "canceled", etc.
                if (execType != "trade" && execType != "filled") continue;

                var orderId = item["order_id"]?.GetValue<string>();
                var lastQty = item["last_qty"]?.GetValue<decimal>() ?? 0m;
                var lastPrice = item["last_price"]?.GetValue<decimal>() ?? 0m;
                var avgPrice = item["avg_price"]?.GetValue<decimal>() ?? lastPrice;
                var timestampStr = item["timestamp"]?.GetValue<string>();
                var isFullFill = orderStatus == "filled" || execType == "filled";
                var ts = timestampStr is not null
                    ? DateTimeOffset.Parse(timestampStr, null,
                        System.Globalization.DateTimeStyles.RoundtripKind)
                    : DateTimeOffset.UtcNow;

                if (orderId is null || lastQty == 0m)
                {
                    _logger.LogWarning("KrakenWsExecutionAdapter: skipped malformed execution: {Json}", json);
                    continue;
                }

                var capturedOrderId = orderId;
                var capturedQty = lastQty;
                var capturedPrice = avgPrice;  // Use avg_price for position cost basis accuracy.
                var capturedFull = isFullFill;
                var capturedTs = ts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _router.ApplyFillByExchangeOrderIdAsync(
                            capturedOrderId,
                            capturedQty,
                            capturedPrice,
                            capturedFull,
                            capturedTs,
                            mode: "live");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "KrakenWsExecutionAdapter: error applying fill for order {OrderId}.",
                            capturedOrderId);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KrakenWsExecutionAdapter: failed to parse message: {Json}", json);
        }
    }
}
