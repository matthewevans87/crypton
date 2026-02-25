using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MarketDataService.Models;

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

    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _nextRequestTime = DateTime.MinValue;
    private const int MaxRequestsPerSecond = 5;
    private const int MaxRequestsPerMinute = 15;

    private int _requestsThisMinute = 0;
    private DateTime _minuteWindowStart = DateTime.UtcNow;

    public string ExchangeName => "Kraken";
    public bool IsConnected => _isConnected;
    
    public event EventHandler<PriceTicker>? OnPriceUpdate;
    public event EventHandler<OrderBook>? OnOrderBookUpdate;
    public event EventHandler<Trade>? OnTrade;
    public event EventHandler<bool>? OnConnectionStateChanged;

    private static readonly Dictionary<string, string> SymbolMapping = new()
    {
        { "BTC/USD", "XBT/USD" },
        { "ETH/USD", "ETH/USD" },
        { "SOL/USD", "SOL/USD" }
    };

    public KrakenExchangeAdapter(HttpClient httpClient, ILogger<KrakenExchangeAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://api.kraken.com");
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

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _webSocket = new ClientWebSocket();
            _webSocketCts = new CancellationTokenSource();
            
            await _webSocket.ConnectAsync(new Uri("wss://ws.kraken.com"), cancellationToken);
            
            _isConnected = true;
            _lastConnectedAt = DateTime.UtcNow;
            OnConnectionStateChanged?.Invoke(this, true);
            
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

    public async Task DisconnectAsync()
    {
        _webSocketCts?.Cancel();
        
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
            }
            catch (Exception ex)
            {
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
        await WaitForRateLimitAsync(cancellationToken);
        
        try
        {
            var krakenSymbol = SymbolMapping.GetValueOrDefault(symbol, symbol);
            var response = await _httpClient.GetAsync($"/0/public/Depth?pair={krakenSymbol}&count={depth}", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
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
        }
        catch (Exception ex)
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OHLCV for {Symbol}", symbol);
        }
        
        return result.TakeLast(limit).ToList();
    }

    public async Task<List<Balance>> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        await WaitForRateLimitAsync(cancellationToken);
        
        var balances = new List<Balance>();
        
        try
        {
            var response = await _httpClient.PostAsync("/0/private/Balance", null, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (data.TryGetProperty("result", out var result))
                {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get balance");
        }
        
        return balances;
    }

    public async Task<List<Trade>> GetTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default)
    {
        return new List<Trade>();
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
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(message);
            
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
}
