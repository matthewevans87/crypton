using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.Extensions.Configuration;

namespace MarketDataService.Adapters;

public class KrakenExchangeAdapter : IExchangeAdapter
{
    private readonly ILogger<KrakenExchangeAdapter> _logger;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _webSocketCts;
    private readonly HashSet<string> _subscribedSymbols = new();
    private readonly object _lock = new();
    private bool _isConnected;
    private int _reconnectCount;
    private DateTime? _lastConnectedAt;
    private bool _shouldReconnect = true;

    private readonly string? _apiKey;
    private readonly string? _apiSecret;

    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _nextRequestTime = DateTime.MinValue;
    private const int MaxRequestsPerSecond = 5;
    private const int MaxRequestsPerMinute = 15;

    private int _requestsThisMinute = 0;
    private DateTime _minuteWindowStart = DateTime.UtcNow;

    private readonly TimeSpan _initialReconnectDelay = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _maxReconnectDelay = TimeSpan.FromSeconds(60);
    private readonly double _backoffMultiplier = 2.0;
    private readonly double _jitterFactor = 0.2;
    private TimeSpan _currentReconnectDelay;
    private int _consecutiveFailures;
    private readonly object _reconnectLock = new();
    private readonly CircuitBreaker _circuitBreaker;

    public string ExchangeName => "Kraken";
    public bool IsConnected => _isConnected;
    public int ReconnectCount => _reconnectCount;
    public TimeSpan CurrentReconnectDelay => _currentReconnectDelay;
    public CircuitBreakerState CircuitBreakerState => _circuitBreaker.State;

    public event EventHandler<PriceTicker>? OnPriceUpdate;
    public event EventHandler<OrderBook>? OnOrderBookUpdate;
    public event EventHandler<Trade>? OnTrade;
    public event EventHandler<List<Balance>>? OnBalanceUpdate;
    public event EventHandler<bool>? OnConnectionStateChanged;

    private static readonly Dictionary<string, string> SymbolMapping = new()
    {
        { "BTC/USD", "XXBTZUSD" },
        { "ETH/USD", "XETHZUSD" },
        { "SOL/USD", "SOLUSD" }
    };

    public KrakenExchangeAdapter(HttpClient httpClient, ILogger<KrakenExchangeAdapter> logger, ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _httpClient.BaseAddress = new Uri(configuration["Kraken:RestBaseUrl"] ?? "https://api.kraken.com");
        _currentReconnectDelay = _initialReconnectDelay;

        // Keys are injected via env vars Kraken__ApiKey and Kraken__ApiSecret,
        // which IConfiguration maps to Kraken:ApiKey and Kraken:ApiSecret.
        _apiKey = configuration["Kraken:ApiKey"];
        _apiSecret = configuration["Kraken:ApiSecret"];

        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
        {
            _logger.LogWarning("Kraken API keys not configured - private API calls will not work");
        }
        else
        {
            _logger.LogInformation("Kraken API key configured");
        }

        var circuitBreakerLogger = _loggerFactory.CreateLogger<CircuitBreaker>();
        _circuitBreaker = new CircuitBreaker(new CircuitBreakerOptions(), circuitBreakerLogger);
    }

    private readonly ILoggerFactory _loggerFactory;

    private Dictionary<string, string> GetKrakenAuthHeaders(string endpoint, string nonce, string postData = "")
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
        {
            return new Dictionary<string, string>();
        }

        // SHA256(nonce + urlencoded_postdata)
        var encodedPayload = nonce + postData;
        var sha256Hash = SHA256.HashData(Encoding.UTF8.GetBytes(encodedPayload));

        // HMAC-SHA512(secret, path_bytes + sha256_raw_bytes)
        var path = endpoint;
        var secretBytes = Convert.FromBase64String(_apiSecret);
        using var hmac = new HMACSHA512(secretBytes);

        var signatureInput = Encoding.UTF8.GetBytes(path).Concat(sha256Hash).ToArray();
        var signature = hmac.ComputeHash(signatureInput);

        return new Dictionary<string, string>
        {
            { "API-Key", _apiKey },
            { "API-Sign", Convert.ToBase64String(signature) }
        };
    }

    private TimeSpan CalculateReconnectDelay()
    {
        var delayWithJitter = _currentReconnectDelay.TotalSeconds * _jitterFactor * (Random.Shared.NextDouble() * 2 - 1);
        var finalDelay = _currentReconnectDelay.TotalSeconds + delayWithJitter;
        return TimeSpan.FromSeconds(Math.Max(0, Math.Min(finalDelay, _maxReconnectDelay.TotalSeconds)));
    }

    private void ResetReconnectDelay()
    {
        lock (_reconnectLock)
        {
            _currentReconnectDelay = _initialReconnectDelay;
            _consecutiveFailures = 0;
        }
    }

    private void IncreaseReconnectDelay()
    {
        lock (_reconnectLock)
        {
            _consecutiveFailures++;
            _currentReconnectDelay = TimeSpan.FromSeconds(
                Math.Min(
                    _currentReconnectDelay.TotalSeconds * _backoffMultiplier,
                    _maxReconnectDelay.TotalSeconds
                )
            );
            _logger.LogInformation("Reconnect delay increased to {Delay}s after {FailureCount} consecutive failures",
                _currentReconnectDelay.TotalSeconds, _consecutiveFailures);
        }
    }

    private async Task StartReconnectLoopAsync(CancellationToken cancellationToken)
    {
        while (_shouldReconnect && !cancellationToken.IsCancellationRequested)
        {
            var delay = CalculateReconnectDelay();
            _logger.LogInformation("Attempting to reconnect in {Delay:F1} seconds (attempt #{Attempt})",
                delay.TotalSeconds, _reconnectCount + 1);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _reconnectCount++;
            var connected = await ConnectInternalAsync(cancellationToken);

            if (connected)
            {
                ResetReconnectDelay();
                _logger.LogInformation("Successfully reconnected after {Attempt} attempts", _reconnectCount);
                break;
            }
            else
            {
                IncreaseReconnectDelay();
            }
        }
    }

    private async Task<bool> ConnectInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            _webSocket = new ClientWebSocket();
            _webSocketCts = new CancellationTokenSource();

            await _webSocket.ConnectAsync(new Uri("wss://ws.kraken.com"), cancellationToken);

            _isConnected = true;
            _lastConnectedAt = DateTime.UtcNow;
            OnConnectionStateChanged?.Invoke(this, true);

            await SubscribeToChannelsAsync(cancellationToken);

            _ = Task.Run(() => ReceiveMessagesAsync(_webSocketCts.Token), _webSocketCts.Token);

            _logger.LogInformation("Connected to Kraken WebSocket");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Kraken WebSocket");
            _isConnected = false;
            OnConnectionStateChanged?.Invoke(this, false);
            return false;
        }
    }

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            if (_requestsThisMinute >= MaxRequestsPerMinute)
            {
                var timeSinceWindowStart = DateTime.UtcNow - _minuteWindowStart;
                if (timeSinceWindowStart < TimeSpan.FromMinutes(1))
                {
                    var waitTime = TimeSpan.FromMinutes(1) - timeSinceWindowStart;
                    _logger.LogWarning("Rate limit reached, waiting {WaitTime}s", waitTime.TotalSeconds);
                    await Task.Delay(waitTime, cancellationToken);
                    _requestsThisMinute = 0;
                    _minuteWindowStart = DateTime.UtcNow;
                }
            }

            var now = DateTime.UtcNow;
            if (now - _nextRequestTime < TimeSpan.FromMilliseconds(1000 / MaxRequestsPerSecond))
            {
                var delay = TimeSpan.FromMilliseconds(1000 / MaxRequestsPerSecond) - (now - _nextRequestTime);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            _nextRequestTime = DateTime.UtcNow;
            _requestsThisMinute++;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private async Task SubscribeToChannelsAsync(CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var krakenSymbols = SymbolMapping.Values.ToList();

        var subscribeMessage = new
        {
            @event = "subscribe",
            pair = krakenSymbols,
            subscription = new { name = "ticker" }
        };

        var json = JsonSerializer.Serialize(subscribeMessage);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

        _logger.LogInformation("Subscribed to ticker channel for {Symbols}", string.Join(", ", krakenSymbols));

        var bookSubscribeMessage = new
        {
            @event = "subscribe",
            pair = krakenSymbols,
            subscription = new { name = "book", depth = 25 }
        };

        json = JsonSerializer.Serialize(bookSubscribeMessage);
        bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

        _logger.LogInformation("Subscribed to book channel for {Symbols}", string.Join(", ", krakenSymbols));

        var tradeSubscribeMessage = new
        {
            @event = "subscribe",
            pair = krakenSymbols,
            subscription = new { name = "trade" }
        };

        json = JsonSerializer.Serialize(tradeSubscribeMessage);
        bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

        _logger.LogInformation("Subscribed to trade channel for {Symbols}", string.Join(", ", krakenSymbols));
    }

    private CancellationTokenSource? _balanceUpdateCts;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _shouldReconnect = true;

        var connected = await ConnectInternalAsync(cancellationToken);

        if (!connected)
        {
            _logger.LogWarning("Initial connection failed, starting reconnect loop");
            _ = Task.Run(() => StartReconnectLoopAsync(cancellationToken), cancellationToken);
        }
        else
        {
            StartBalanceUpdateTimer();
        }

        return connected;
    }

    private void StartBalanceUpdateTimer()
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
        {
            _logger.LogDebug("Skipping balance updates - API keys not configured");
            return;
        }

        _balanceUpdateCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_balanceUpdateCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _balanceUpdateCts.Token);
                    var balances = await GetBalanceAsync(_balanceUpdateCts.Token);
                    if (balances.Count > 0)
                    {
                        OnBalanceUpdate?.Invoke(this, balances);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching balance for updates");
                }
            }
        }, _balanceUpdateCts.Token);
    }

    public async Task DisconnectAsync()
    {
        _shouldReconnect = false;
        _webSocketCts?.Cancel();
        _balanceUpdateCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
        }

        _isConnected = false;
        OnConnectionStateChanged?.Invoke(this, false);
        _logger.LogInformation("Disconnected from Kraken WebSocket");
    }

    public async Task<PriceTicker?> GetPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var prices = await GetPricesAsync(new[] { symbol }, cancellationToken);
        return prices.FirstOrDefault();
    }

    public async Task<List<PriceTicker>> GetPricesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanExecute())
        {
            _logger.LogWarning("Circuit breaker is open, rejecting request");
            throw new CircuitBreakerOpenException("Circuit breaker is open - exchange API unavailable");
        }

        await WaitForRateLimitAsync(cancellationToken);

        var result = new List<PriceTicker>();

        foreach (var symbol in symbols)
        {
            try
            {
                var krakenSymbol = SymbolMapping.GetValueOrDefault(symbol, symbol);
                var response = await _httpClient.GetAsync($"/0/public/Ticker?pair={krakenSymbol}", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _circuitBreaker.RecordSuccess();
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var data = JsonSerializer.Deserialize<JsonElement>(json);

                    if (data.TryGetProperty("result", out var resultObj))
                    {
                        foreach (var prop in resultObj.EnumerateObject())
                        {
                            var ticker = ParseTicker(prop.Name, prop.Value, symbol);
                            if (ticker != null)
                            {
                                result.Add(ticker);
                            }
                        }
                    }
                }
                else
                {
                    _circuitBreaker.RecordFailure();
                }
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure();
                _logger.LogError(ex, "Failed to get price for {Symbol}", symbol);
            }
        }

        return result;
    }

    private PriceTicker? ParseTicker(string krakenPair, JsonElement data, string originalSymbol)
    {
        try
        {
            var ticker = new PriceTicker
            {
                Asset = originalSymbol,
                LastUpdated = DateTime.UtcNow
            };

            if (data.TryGetProperty("c", out var close) && close.GetArrayLength() >= 2)
            {
                ticker.Price = decimal.Parse(close[0].GetString() ?? "0");
            }

            if (data.TryGetProperty("b", out var bid) && bid.GetArrayLength() >= 2)
            {
                ticker.Bid = decimal.Parse(bid[0].GetString() ?? "0");
            }

            if (data.TryGetProperty("a", out var ask) && ask.GetArrayLength() >= 2)
            {
                ticker.Ask = decimal.Parse(ask[0].GetString() ?? "0");
            }

            if (data.TryGetProperty("v", out var volume) && volume.GetArrayLength() >= 2)
            {
                ticker.Volume24h = decimal.Parse(volume[1].GetString() ?? "0");
            }

            if (data.TryGetProperty("h", out var high) && high.GetArrayLength() >= 2)
            {
                ticker.High24h = decimal.Parse(high[1].GetString() ?? "0");
            }

            if (data.TryGetProperty("l", out var low) && low.GetArrayLength() >= 2)
            {
                ticker.Low24h = decimal.Parse(low[1].GetString() ?? "0");
            }

            if (data.TryGetProperty("p", out var vwap) && vwap.GetArrayLength() >= 2)
            {
                var vwapValue = decimal.Parse(vwap[1].GetString() ?? "0");
                if (vwapValue > 0)
                {
                    ticker.Change24h = ticker.Price - vwapValue;
                    ticker.ChangePercent24h = (ticker.Change24h / vwapValue) * 100;
                }
            }

            return ticker;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse ticker for {Symbol}", krakenPair);
            return null;
        }
    }

    public async Task<OrderBook?> GetOrderBookAsync(string symbol, int depth = 10, CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanExecute())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open - exchange API unavailable");
        }

        await WaitForRateLimitAsync(cancellationToken);

        try
        {
            var krakenSymbol = SymbolMapping.GetValueOrDefault(symbol, symbol);
            var response = await _httpClient.GetAsync($"/0/public/Depth?pair={krakenSymbol}&count={depth}", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordSuccess();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("result", out var result))
                {
                    foreach (var prop in result.EnumerateObject())
                    {
                        return ParseOrderBook(prop.Name, prop.Value, symbol);
                    }
                }
            }
            else
            {
                _circuitBreaker.RecordFailure();
            }
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Failed to get order book for {Symbol}", symbol);
        }

        return null;
    }

    private OrderBook ParseOrderBook(string krakenPair, JsonElement data, string symbol)
    {
        var orderBook = new OrderBook { Symbol = symbol };

        if (data.TryGetProperty("bids", out var bids))
        {
            foreach (var bid in bids.EnumerateArray())
            {
                if (bid.GetArrayLength() >= 3)
                {
                    orderBook.Bids.Add(new OrderBookEntry
                    {
                        Price = decimal.Parse(bid[0].GetString() ?? "0"),
                        Quantity = decimal.Parse(bid[1].GetString() ?? "0"),
                        Count = int.Parse(bid[2].GetString() ?? "0")
                    });
                }
            }
        }

        if (data.TryGetProperty("asks", out var asks))
        {
            foreach (var ask in asks.EnumerateArray())
            {
                if (ask.GetArrayLength() >= 3)
                {
                    orderBook.Asks.Add(new OrderBookEntry
                    {
                        Price = decimal.Parse(ask[0].GetString() ?? "0"),
                        Quantity = decimal.Parse(ask[1].GetString() ?? "0"),
                        Count = int.Parse(ask[2].GetString() ?? "0")
                    });
                }
            }
        }

        return orderBook;
    }

    public async Task<List<Ohlcv>> GetOhlcvAsync(string symbol, string timeframe, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanExecute())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open - exchange API unavailable");
        }

        await WaitForRateLimitAsync(cancellationToken);

        var result = new List<Ohlcv>();

        try
        {
            var krakenSymbol = SymbolMapping.GetValueOrDefault(symbol, symbol);
            var interval = timeframe switch
            {
                "1m" => 1,
                "5m" => 5,
                "15m" => 15,
                "1h" => 60,
                "4h" => 240,
                "1d" => 1440,
                _ => 60
            };

            var response = await _httpClient.GetAsync($"/0/public/OHLC?pair={krakenSymbol}&interval={interval}", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordSuccess();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("result", out var resultObj))
                {
                    foreach (var prop in resultObj.EnumerateObject())
                    {
                        if (prop.Name == "last") continue;

                        foreach (var candle in prop.Value.EnumerateArray())
                        {
                            if (candle.GetArrayLength() >= 6)
                            {
                                result.Add(new Ohlcv
                                {
                                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(candle[0].GetInt64()).UtcDateTime,
                                    Open = decimal.Parse(candle[1].GetString() ?? "0"),
                                    High = decimal.Parse(candle[2].GetString() ?? "0"),
                                    Low = decimal.Parse(candle[3].GetString() ?? "0"),
                                    Close = decimal.Parse(candle[4].GetString() ?? "0"),
                                    Volume = decimal.Parse(candle[6].GetString() ?? "0")
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                _circuitBreaker.RecordFailure();
            }
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Failed to get OHLCV for {Symbol}", symbol);
        }

        return result.TakeLast(limit).ToList();
    }

    public async Task<List<Balance>> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanExecute())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open - exchange API unavailable");
        }

        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
        {
            _logger.LogWarning("Cannot get balance - Kraken API keys not configured");
            return new List<Balance>();
        }

        await WaitForRateLimitAsync(cancellationToken);

        var balances = new List<Balance>();
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var postData = "nonce=" + nonce;

        try
        {
            var headers = GetKrakenAuthHeaders("/0/private/Balance", nonce, postData);
            var request = new HttpRequestMessage(HttpMethod.Post, "/0/private/Balance")
            {
                Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordSuccess();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("error", out var errorArray) && errorArray.GetArrayLength() > 0)
                {
                    _logger.LogWarning("Kraken API error: {Error}", errorArray);
                }

                if (data.TryGetProperty("result", out var result))
                {
                    _logger.LogInformation("Balance result: {Result}", result);
                    foreach (var prop in result.EnumerateObject())
                    {
                        var value = decimal.Parse(prop.Value.GetString() ?? "0");
                        if (value > 0)
                        {
                            balances.Add(new Balance
                            {
                                Asset = prop.Name.StartsWith("X") ? prop.Name[1..] : prop.Name,
                                Available = value
                            });
                        }
                    }
                }
            }
            else
            {
                _circuitBreaker.RecordFailure();
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to get balance: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Failed to get balance");
        }

        return balances;
    }

    public async Task<List<Trade>> GetTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!_circuitBreaker.CanExecute())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open - exchange API unavailable");
        }

        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
        {
            _logger.LogWarning("Cannot get trades - Kraken API keys not configured");
            return new List<Trade>();
        }

        await WaitForRateLimitAsync(cancellationToken);

        var trades = new List<Trade>();
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        try
        {
            var krakenSymbol = SymbolMapping.GetValueOrDefault(symbol, symbol);
            var postData = "nonce=" + nonce + "&pair=" + krakenSymbol;

            var headers = GetKrakenAuthHeaders("/0/private/TradesHistory", nonce, postData);
            var request = new HttpRequestMessage(HttpMethod.Post, "/0/private/TradesHistory")
            {
                Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordSuccess();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("result", out var result))
                {
                    if (result.TryGetProperty("trades", out var tradesObj))
                    {
                        foreach (var prop in tradesObj.EnumerateObject())
                        {
                            var tradeId = prop.Name;
                            var tradeData = prop.Value;

                            if (tradeData.GetArrayLength() >= 4)
                            {
                                trades.Add(new Trade
                                {
                                    Id = tradeId,
                                    Symbol = symbol,
                                    Price = decimal.Parse(tradeData[0].GetString() ?? "0"),
                                    Quantity = decimal.Parse(tradeData[1].GetString() ?? "0"),
                                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(
                                        long.Parse(tradeData[2].GetString() ?? "0")
                                    ).UtcDateTime,
                                    Side = tradeData[3].GetString() ?? ""
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                _circuitBreaker.RecordFailure();
                _logger.LogWarning("Failed to get trades: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Failed to get trades for {Symbol}", symbol);
        }

        return trades.OrderByDescending(t => t.Timestamp).Take(limit).ToList();
    }

    public async Task<PortfolioSummary> GetPortfolioSummaryAsync(CancellationToken cancellationToken = default)
    {
        var balances = await GetBalanceAsync(cancellationToken);

        var summary = new PortfolioSummary
        {
            Balances = balances,
            TotalValue = balances.Sum(b => b.Total),
            LastUpdated = DateTime.UtcNow
        };

        return summary;
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _isConnected = false;
                    OnConnectionStateChanged?.Invoke(this, false);
                    _logger.LogWarning("Kraken WebSocket closed");
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving WebSocket messages");
            _isConnected = false;
            OnConnectionStateChanged?.Invoke(this, false);
        }

        if (_shouldReconnect && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WebSocket disconnected, starting reconnection...");
            IncreaseReconnectDelay();
            _ = Task.Run(() => StartReconnectLoopAsync(cancellationToken), cancellationToken);
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(message);

            // Skip if not an object (e.g., trade updates are arrays)
            if (data.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (data.TryGetProperty("event", out var eventType))
            {
                var eventName = eventType.GetString();

                if (eventName == "heartbeat")
                {
                    return;
                }

                if (eventName == "systemStatus")
                {
                    _logger.LogInformation("Kraken system status: {Status}", data.TryGetProperty("status", out var status) ? status.GetString() : "unknown");
                    return;
                }

                if (eventName == "subscriptionStatus")
                {
                    var channelName = data.TryGetProperty("channelName", out var cn) ? cn.GetString() : "";
                    _logger.LogInformation("Subscribed to {Channel}", channelName);
                    return;
                }
            }

            if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() >= 4)
            {
                var channelName = data[2].GetString();

                if (channelName?.StartsWith("ticker") == true)
                {
                    var tickerData = data[1];
                    var ticker = ParseTickerUpdate(tickerData, channelName);
                    if (ticker != null)
                    {
                        OnPriceUpdate?.Invoke(this, ticker);
                    }
                }

                if (channelName?.StartsWith("book") == true)
                {
                    var bookData = data[1];
                    var orderBook = ParseOrderBookUpdate(bookData, channelName);
                    if (orderBook != null)
                    {
                        OnOrderBookUpdate?.Invoke(this, orderBook);
                    }
                }

                if (channelName?.StartsWith("trade") == true)
                {
                    var tradesData = data[1];
                    var symbol = channelName.Replace("trade", "").Replace("XBT", "BTC");

                    if (tradesData.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tradeArray in tradesData.EnumerateArray())
                        {
                            var trade = ParseTradeUpdate(tradeArray, symbol);
                            if (trade != null)
                            {
                                OnTrade?.Invoke(this, trade);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket message");
        }
    }

    private PriceTicker? ParseTickerUpdate(JsonElement data, string channelName)
    {
        try
        {
            var symbol = channelName.Replace("ticker", "").Replace("XBT", "BTC");

            var ticker = new PriceTicker
            {
                Asset = symbol,
                LastUpdated = DateTime.UtcNow
            };

            if (data.TryGetProperty("c", out var close) && close.GetArrayLength() >= 1)
            {
                ticker.Price = decimal.Parse(close[0].GetString() ?? "0");
            }

            if (data.TryGetProperty("b", out var bid) && bid.GetArrayLength() >= 1)
            {
                ticker.Bid = decimal.Parse(bid[0].GetString() ?? "0");
            }

            if (data.TryGetProperty("a", out var ask) && ask.GetArrayLength() >= 1)
            {
                ticker.Ask = decimal.Parse(ask[0].GetString() ?? "0");
            }

            if (data.TryGetProperty("v", out var volume) && volume.GetArrayLength() >= 2)
            {
                ticker.Volume24h = decimal.Parse(volume[1].GetString() ?? "0");
            }

            if (data.TryGetProperty("h", out var high) && high.GetArrayLength() >= 2)
            {
                ticker.High24h = decimal.Parse(high[1].GetString() ?? "0");
            }

            if (data.TryGetProperty("l", out var low) && low.GetArrayLength() >= 2)
            {
                ticker.Low24h = decimal.Parse(low[1].GetString() ?? "0");
            }

            if (data.TryGetProperty("p", out var vwap) && vwap.GetArrayLength() >= 2)
            {
                var vwapValue = decimal.Parse(vwap[1].GetString() ?? "0");
                if (vwapValue > 0)
                {
                    ticker.Change24h = ticker.Price - vwapValue;
                    ticker.ChangePercent24h = (ticker.Change24h / vwapValue) * 100;
                }
            }

            return ticker;
        }
        catch
        {
            return null;
        }
    }

    private OrderBook? ParseOrderBookUpdate(JsonElement data, string channelName)
    {
        try
        {
            var symbol = channelName.Replace("book", "").Replace("XBT", "BTC");

            var orderBook = new OrderBook
            {
                Symbol = symbol,
                LastUpdated = DateTime.UtcNow
            };

            if (data.TryGetProperty("as", out var asks))
            {
                foreach (var ask in asks.EnumerateArray())
                {
                    if (ask.GetArrayLength() >= 3)
                    {
                        orderBook.Asks.Add(new OrderBookEntry
                        {
                            Price = decimal.Parse(ask[0].GetString() ?? "0"),
                            Quantity = decimal.Parse(ask[1].GetString() ?? "0"),
                            Count = int.Parse(ask[2].GetString() ?? "0")
                        });
                    }
                }
            }

            if (data.TryGetProperty("bs", out var bids))
            {
                foreach (var bid in bids.EnumerateArray())
                {
                    if (bid.GetArrayLength() >= 3)
                    {
                        orderBook.Bids.Add(new OrderBookEntry
                        {
                            Price = decimal.Parse(bid[0].GetString() ?? "0"),
                            Quantity = decimal.Parse(bid[1].GetString() ?? "0"),
                            Count = int.Parse(bid[2].GetString() ?? "0")
                        });
                    }
                }
            }

            if (data.TryGetProperty("a", out var asksUpdate))
            {
                foreach (var ask in asksUpdate.EnumerateArray())
                {
                    if (ask.GetArrayLength() >= 3)
                    {
                        var price = decimal.Parse(ask[0].GetString() ?? "0");
                        var quantity = decimal.Parse(ask[1].GetString() ?? "0");

                        var existing = orderBook.Asks.FirstOrDefault(a => a.Price == price);
                        if (quantity == 0)
                        {
                            if (existing != null)
                                orderBook.Asks.Remove(existing);
                        }
                        else
                        {
                            if (existing != null)
                                existing.Quantity = quantity;
                            else
                                orderBook.Asks.Add(new OrderBookEntry { Price = price, Quantity = quantity, Count = 1 });
                        }
                    }
                }
            }

            if (data.TryGetProperty("b", out var bidsUpdate))
            {
                foreach (var bid in bidsUpdate.EnumerateArray())
                {
                    if (bid.GetArrayLength() >= 3)
                    {
                        var price = decimal.Parse(bid[0].GetString() ?? "0");
                        var quantity = decimal.Parse(bid[1].GetString() ?? "0");

                        var existing = orderBook.Bids.FirstOrDefault(b => b.Price == price);
                        if (quantity == 0)
                        {
                            if (existing != null)
                                orderBook.Bids.Remove(existing);
                        }
                        else
                        {
                            if (existing != null)
                                existing.Quantity = quantity;
                            else
                                orderBook.Bids.Add(new OrderBookEntry { Price = price, Quantity = quantity, Count = 1 });
                        }
                    }
                }
            }

            orderBook.Asks = orderBook.Asks.OrderBy(a => a.Price).Take(25).ToList();
            orderBook.Bids = orderBook.Bids.OrderByDescending(b => b.Price).Take(25).ToList();

            return orderBook;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing orderbook update");
            return null;
        }
    }

    private Trade? ParseTradeUpdate(JsonElement data, string symbol)
    {
        try
        {
            if (data.GetArrayLength() >= 4)
            {
                var timestamp = long.Parse(data[2].GetString() ?? "0");

                return new Trade
                {
                    Id = $"{symbol}_{timestamp}",
                    Symbol = symbol,
                    Price = decimal.Parse(data[0].GetString() ?? "0"),
                    Quantity = decimal.Parse(data[1].GetString() ?? "0"),
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime,
                    Side = data[3].GetString() ?? ""
                };
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
