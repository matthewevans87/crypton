using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Exchange;

/// <summary>
/// Abstraction over all exchange interactions. All execution logic goes through
/// this interface — no code outside of adapter implementations may reference
/// exchange-specific types.
///
/// All methods are async and CancellationToken-aware.
/// Rate limiting and retry are fully encapsulated within each adapter implementation;
/// callers never see raw rate-limit errors.
/// </summary>
public interface IExchangeAdapter
{
    /// <summary>
    /// Subscribe to real-time market data for the given assets.
    /// The callback is invoked on every tick with the latest snapshot.
    /// Calling this again with a new asset set replaces the prior subscription.
    /// </summary>
    Task SubscribeToMarketDataAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken = default);

    /// <summary>Submit an order to the exchange.</summary>
    Task<OrderAcknowledgement> PlaceOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Cancel a previously placed order.</summary>
    Task<CancellationResult> CancelOrderAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>Query the current status (and fill info) of a specific order.</summary>
    Task<OrderStatusResult> GetOrderStatusAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>Get the current account balance.</summary>
    Task<AccountBalance> GetAccountBalanceAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Get all currently open positions on the exchange.</summary>
    Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Get trade history since the specified timestamp.</summary>
    Task<IReadOnlyList<Trade>> GetTradeHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The current rate-limit state of this adapter.
    /// True = currently in back-off; callers should expect delayed responses.
    /// </summary>
    bool IsRateLimited { get; }

    /// <summary>
    /// If currently rate-limited, the approximate time at which normal
    /// operation will resume. Null if not currently rate-limited.
    /// </summary>
    DateTimeOffset? RateLimitResumesAt { get; }
}
