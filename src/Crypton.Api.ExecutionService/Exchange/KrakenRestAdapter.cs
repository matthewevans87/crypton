using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crypton.Api.ExecutionService.Models;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Exchange;

/// <summary>
/// Handles all order/account operations via the Kraken REST API v0.
/// Market data subscriptions are not supported; use <see cref="KrakenWebSocketAdapter"/> instead.
/// </summary>
public sealed class KrakenRestAdapter : IExchangeAdapter
{
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly HttpClient _httpClient;
    private readonly ILogger<KrakenRestAdapter> _logger;

    private readonly Lock _rateLimitLock = new();
    private bool _isRateLimited;
    private DateTimeOffset? _rateLimitResumesAt;

    public KrakenRestAdapter(
        string apiKey,
        string apiSecret,
        HttpClient httpClient,
        ILogger<KrakenRestAdapter> logger)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsRateLimited
    {
        get
        {
            lock (_rateLimitLock)
            {
                if (_isRateLimited && DateTimeOffset.UtcNow > _rateLimitResumesAt)
                {
                    _isRateLimited = false;
                    _rateLimitResumesAt = null;
                }
                return _isRateLimited;
            }
        }
    }

    public DateTimeOffset? RateLimitResumesAt
    {
        get
        {
            lock (_rateLimitLock)
            {
                return _rateLimitResumesAt;
            }
        }
    }

    // -----------------------------------------------------------------------
    // PlaceOrderAsync
    // -----------------------------------------------------------------------

    public async Task<OrderAcknowledgement> PlaceOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderType = request.Type switch
        {
            OrderType.Market => "market",
            OrderType.Limit => "limit",
            OrderType.StopLoss => "stop-loss",
            OrderType.StopLossLimit => "stop-loss-limit",
            _ => "market"
        };
        var side = request.Side == OrderSide.Buy ? "buy" : "sell";

        var fields = new Dictionary<string, string>
        {
            ["ordertype"] = orderType,
            ["type"] = side,
            ["volume"] = request.Quantity.ToString("G"),
            ["pair"] = request.Asset,
        };

        if (request.LimitPrice.HasValue)
            fields["price"] = request.LimitPrice.Value.ToString("G");

        var response = await PostPrivateAsync("/0/private/AddOrder", fields, cancellationToken);

        var errors = response.RootElement.GetProperty("error").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            var errMsg = errors[0].GetString() ?? "Unknown error";
            HandleKrakenError(errMsg);
        }

        var result = response.RootElement.GetProperty("result");
        var txid = result.GetProperty("txids").EnumerateArray().First().GetString()
                   ?? throw new ExchangeAdapterException("Kraken returned no txid.");

        return new OrderAcknowledgement
        {
            InternalId = request.InternalId,
            ExchangeOrderId = txid,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    // -----------------------------------------------------------------------
    // CancelOrderAsync
    // -----------------------------------------------------------------------

    public async Task<CancellationResult> CancelOrderAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["txid"] = exchangeOrderId
        };

        var response = await PostPrivateAsync("/0/private/CancelOrder", fields, cancellationToken);

        var errors = response.RootElement.GetProperty("error").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            var errMsg = errors[0].GetString() ?? "Unknown error";
            return new CancellationResult
            {
                ExchangeOrderId = exchangeOrderId,
                Success = false,
                ErrorMessage = errMsg
            };
        }

        return new CancellationResult
        {
            ExchangeOrderId = exchangeOrderId,
            Success = true
        };
    }

    // -----------------------------------------------------------------------
    // GetOrderStatusAsync
    // -----------------------------------------------------------------------

    public async Task<OrderStatusResult> GetOrderStatusAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["txid"] = exchangeOrderId
        };

        var response = await PostPrivateAsync("/0/private/QueryOrders", fields, cancellationToken);

        var errors = response.RootElement.GetProperty("error").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            var errMsg = errors[0].GetString() ?? "Unknown error";
            HandleKrakenError(errMsg);
        }

        var result = response.RootElement.GetProperty("result");
        if (!result.TryGetProperty(exchangeOrderId, out var orderEl))
            throw new OrderNotFoundException(exchangeOrderId);

        var statusStr = orderEl.TryGetProperty("status", out var statusEl)
            ? statusEl.GetString() ?? "unknown"
            : "unknown";

        var status = statusStr switch
        {
            "open" => OrderStatus.Open,
            "closed" => OrderStatus.Filled,
            "canceled" => OrderStatus.Cancelled,
            "pending" => OrderStatus.Pending,
            _ => OrderStatus.Open
        };

        var filledQty = 0m;
        if (orderEl.TryGetProperty("vol_exec", out var volExecEl))
            decimal.TryParse(volExecEl.GetString(), out filledQty);

        decimal? avgPrice = null;
        if (orderEl.TryGetProperty("price", out var priceEl) && priceEl.GetString() is string ps && decimal.TryParse(ps, out var ap) && ap > 0)
            avgPrice = ap;

        return new OrderStatusResult
        {
            ExchangeOrderId = exchangeOrderId,
            Status = status,
            FilledQuantity = filledQty,
            AverageFillPrice = avgPrice
        };
    }

    // -----------------------------------------------------------------------
    // GetAccountBalanceAsync
    // -----------------------------------------------------------------------

    public async Task<AccountBalance> GetAccountBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await PostPrivateAsync("/0/private/Balance", new Dictionary<string, string>(), cancellationToken);

        var errors = response.RootElement.GetProperty("error").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            var errMsg = errors[0].GetString() ?? "Unknown error";
            HandleKrakenError(errMsg);
        }

        var result = response.RootElement.GetProperty("result");

        var assetBalances = new Dictionary<string, decimal>();
        decimal availableUsd = 0m;

        foreach (var prop in result.EnumerateObject())
        {
            if (decimal.TryParse(prop.Value.GetString(), out var val))
            {
                var key = prop.Name;
                assetBalances[key] = val;

                // Map ZUSD or USD to available cash
                if (key is "ZUSD" or "USD")
                    availableUsd = val;
            }
        }

        return new AccountBalance
        {
            AvailableUsd = availableUsd,
            AssetBalances = assetBalances,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    // -----------------------------------------------------------------------
    // GetOpenPositionsAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await PostPrivateAsync("/0/private/OpenPositions", new Dictionary<string, string>(), cancellationToken);

        var errors = response.RootElement.GetProperty("error").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            var errMsg = errors[0].GetString() ?? "Unknown error";
            HandleKrakenError(errMsg);
        }

        var result = response.RootElement.GetProperty("result");
        var positions = new List<ExchangePosition>();

        foreach (var prop in result.EnumerateObject())
        {
            var pos = prop.Value;
            var asset = pos.TryGetProperty("pair", out var pairEl) ? pairEl.GetString() ?? prop.Name : prop.Name;
            var direction = pos.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "buy" : "buy";
            var qty = pos.TryGetProperty("vol", out var volEl) && decimal.TryParse(volEl.GetString(), out var v) ? v : 0m;
            var entryPrice = pos.TryGetProperty("cost", out var costEl) && decimal.TryParse(costEl.GetString(), out var c) && qty != 0 ? c / qty : 0m;

            positions.Add(new ExchangePosition
            {
                Asset = asset,
                Direction = direction,
                Quantity = qty,
                AverageEntryPrice = entryPrice
            });
        }

        return positions;
    }

    // -----------------------------------------------------------------------
    // GetTradeHistoryAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<Trade>> GetTradeHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["start"] = since.ToUnixTimeSeconds().ToString()
        };

        var response = await PostPrivateAsync("/0/private/TradesHistory", fields, cancellationToken);

        var errors = response.RootElement.GetProperty("error").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            var errMsg = errors[0].GetString() ?? "Unknown error";
            HandleKrakenError(errMsg);
        }

        var result = response.RootElement.GetProperty("result");
        var trades = new List<Trade>();

        if (!result.TryGetProperty("trades", out var tradesEl))
            return trades;

        foreach (var prop in tradesEl.EnumerateObject())
        {
            var t = prop.Value;
            var asset = t.TryGetProperty("pair", out var pairEl) ? pairEl.GetString() ?? string.Empty : string.Empty;
            var typeStr = t.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "buy";
            var side = typeStr == "sell" ? OrderSide.Sell : OrderSide.Buy;
            var qty = t.TryGetProperty("vol", out var volEl) && decimal.TryParse(volEl.GetString(), out var v) ? v : 0m;
            var price = t.TryGetProperty("price", out var priceEl) && decimal.TryParse(priceEl.GetString(), out var p) ? p : 0m;
            var fee = t.TryGetProperty("fee", out var feeEl) && decimal.TryParse(feeEl.GetString(), out var f) ? f : 0m;
            var tsUnix = t.TryGetProperty("time", out var timeEl) ? timeEl.GetDouble() : 0;
            var ts = DateTimeOffset.FromUnixTimeMilliseconds((long)(tsUnix * 1000));

            trades.Add(new Trade
            {
                ExchangeTradeId = prop.Name,
                Asset = asset,
                Side = side,
                Quantity = qty,
                Price = price,
                Fee = fee,
                Timestamp = ts
            });
        }

        return trades;
    }

    // -----------------------------------------------------------------------
    // SubscribeToMarketDataAsync — not supported by REST adapter
    // -----------------------------------------------------------------------

    public Task SubscribeToMarketDataAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use KrakenWebSocketAdapter for market data.");

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<JsonDocument> PostPrivateAsync(
        string path,
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        parameters["nonce"] = nonce;

        var postData = string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var signature = ComputeSignature(path, nonce, postData);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
        request.Headers.Add("API-Key", _apiKey);
        request.Headers.Add("API-Sign", signature);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ExchangeAdapterException($"HTTP request to {path} failed: {ex.Message}", ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthenticationException("Kraken returned 401 Unauthorized. Check API credentials.");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(json);

        // Check for EAPI:Invalid key at the top-level parse
        var errors = doc.RootElement.GetProperty("error").EnumerateArray().ToList();
        foreach (var err in errors)
        {
            if (err.GetString() is "EAPI:Invalid key")
                throw new AuthenticationException("Kraken API key is invalid.");
        }

        return doc;
    }

    private string ComputeSignature(string path, string nonce, string postData)
    {
        // message = path + SHA256(nonce + postData)
        var nonceAndPostData = nonce + postData;
        var sha256Hash = SHA256.HashData(Encoding.UTF8.GetBytes(nonceAndPostData));

        var pathBytes = Encoding.UTF8.GetBytes(path);
        var message = new byte[pathBytes.Length + sha256Hash.Length];
        pathBytes.CopyTo(message, 0);
        sha256Hash.CopyTo(message, pathBytes.Length);

        var secretBytes = Convert.FromBase64String(_apiSecret);
        var hmacHash = HMACSHA512.HashData(secretBytes, message);
        return Convert.ToBase64String(hmacHash);
    }

    private void HandleKrakenError(string errMsg)
    {
        _logger.LogWarning("Kraken API error: {Error}", errMsg);

        if (errMsg.Contains("EOrder:Rate limit exceeded") || errMsg.Contains("EAPI:Rate limit exceeded"))
        {
            var resumesAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
            lock (_rateLimitLock)
            {
                _isRateLimited = true;
                _rateLimitResumesAt = resumesAt;
            }
            throw new RateLimitException(errMsg, resumesAt);
        }

        if (errMsg.Contains("EAPI:Invalid key"))
            throw new AuthenticationException(errMsg);

        throw new ExchangeAdapterException(errMsg);
    }
}
