using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Crypton.Api.ExecutionService.Models;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Exchange;

/// <summary>
/// Provides real-time market data via the Kraken WebSocket API v2.
/// All order/account operations throw <see cref="NotSupportedException"/>;
/// use <see cref="KrakenRestAdapter"/> for those.
/// </summary>
public sealed class KrakenWebSocketAdapter : IExchangeAdapter
{
    private readonly string _wsBaseUrl;
    private readonly int _maxReconnectAttempts;
    private readonly int _reconnectDelaySeconds;
    private readonly Func<ClientWebSocket> _webSocketFactory;
    private readonly ILogger<KrakenWebSocketAdapter> _logger;

    public KrakenWebSocketAdapter(
        string wsBaseUrl,
        int maxReconnectAttempts,
        int reconnectDelaySeconds,
        ILogger<KrakenWebSocketAdapter> logger,
        Func<ClientWebSocket>? webSocketFactory = null)
    {
        _wsBaseUrl = wsBaseUrl;
        _maxReconnectAttempts = maxReconnectAttempts;
        _reconnectDelaySeconds = reconnectDelaySeconds;
        _logger = logger;
        _webSocketFactory = webSocketFactory ?? (() => new ClientWebSocket());
    }

    // WebSocket connections are not rate-limited in the same way.
    public bool IsRateLimited => false;
    public DateTimeOffset? RateLimitResumesAt => null;

    public async Task SubscribeToMarketDataAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken = default)
    {
        int attempts = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndSubscribeAsync(assets, onSnapshot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempts++;
                if (attempts >= _maxReconnectAttempts)
                {
                    _logger.LogError(ex, "WebSocket connection failed after {MaxAttempts} attempts. Giving up.", _maxReconnectAttempts);
                    return;
                }

                _logger.LogWarning(ex,
                    "WebSocket connection dropped (attempt {Attempt}/{Max}). Reconnecting in {Delay}s...",
                    attempts, _maxReconnectAttempts, _reconnectDelaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(_reconnectDelaySeconds), cancellationToken);
            }
        }
    }

    private async Task ConnectAndSubscribeAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken)
    {
        using var ws = _webSocketFactory();
        await ws.ConnectAsync(new Uri(_wsBaseUrl), cancellationToken);
        _logger.LogInformation("Connected to Kraken WebSocket at {Url}", _wsBaseUrl);

        // Subscribe to ticker channel
        var subscriptionMsg = JsonSerializer.Serialize(new
        {
            method = "subscribe",
            @params = new
            {
                channel = "ticker",
                symbol = assets
            }
        });

        var msgBytes = Encoding.UTF8.GetBytes(subscriptionMsg);
        await ws.SendAsync(msgBytes, WebSocketMessageType.Text, true, cancellationToken);
        _logger.LogDebug("Subscribed to ticker for {Assets}", string.Join(", ", assets));

        // Receive loop
        var buffer = new byte[65536];
        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, cancellationToken);
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation("WebSocket closed by server.");
                break;
            }

            var rawJson = Encoding.UTF8.GetString(ms.ToArray());
            await ProcessMessageAsync(rawJson, onSnapshot);
        }
    }

    /// <summary>
    /// Parses a raw WebSocket JSON message and invokes <paramref name="callback"/>
    /// for every ticker data item. Exposed as internal for unit testing.
    /// </summary>
    internal async Task ProcessMessageAsync(string rawJson, Func<MarketSnapshot, Task> callback)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("channel", out var channelEl))
                return;
            if (channelEl.GetString() != "ticker")
                return;

            if (!root.TryGetProperty("data", out var dataEl))
                return;

            foreach (var item in dataEl.EnumerateArray())
            {
                var snapshot = ParseTickerItem(item);
                if (snapshot is not null)
                    await callback(snapshot);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse WebSocket message: {Raw}", rawJson);
        }
    }

    private static MarketSnapshot? ParseTickerItem(JsonElement item)
    {
        if (!item.TryGetProperty("symbol", out var symbolEl)) return null;
        if (!item.TryGetProperty("bid", out var bidEl)) return null;
        if (!item.TryGetProperty("ask", out var askEl)) return null;

        var symbol = symbolEl.GetString() ?? string.Empty;
        var bid = bidEl.GetDecimal();
        var ask = askEl.GetDecimal();

        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        if (item.TryGetProperty("timestamp", out var tsEl) && tsEl.GetString() is string tsStr)
            DateTimeOffset.TryParse(tsStr, out timestamp);

        return new MarketSnapshot
        {
            Asset = symbol,
            Bid = bid,
            Ask = ask,
            Timestamp = timestamp
        };
    }

    // -----------------------------------------------------------------------
    // Order / account methods — not supported by WebSocket adapter
    // -----------------------------------------------------------------------

    private static NotSupportedException NotSupported() =>
        new("WebSocket adapter is for market data only. Use KrakenRestAdapter for orders.");

    public Task<OrderAcknowledgement> PlaceOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
        => throw NotSupported();

    public Task<CancellationResult> CancelOrderAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
        => throw NotSupported();

    public Task<OrderStatusResult> GetOrderStatusAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
        => throw NotSupported();

    public Task<AccountBalance> GetAccountBalanceAsync(
        CancellationToken cancellationToken = default)
        => throw NotSupported();

    public Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(
        CancellationToken cancellationToken = default)
        => throw NotSupported();

    public Task<IReadOnlyList<Trade>> GetTradeHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
        => throw NotSupported();
}
