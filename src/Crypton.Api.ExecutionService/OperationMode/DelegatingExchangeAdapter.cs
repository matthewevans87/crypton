using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Routes all <see cref="IExchangeAdapter"/> calls to either the
/// <see cref="PaperTradingAdapter"/> or a live adapter, determined dynamically
/// by <see cref="OperationModeService.CurrentMode"/> at call time.
///
/// When live, <see cref="SubscribeToMarketDataAsync"/> delegates to the WebSocket
/// adapter (<see cref="KrakenWebSocketAdapter"/>), and all other calls delegate to
/// the REST adapter (<see cref="KrakenRestAdapter"/>).
/// </summary>
public sealed class DelegatingExchangeAdapter : IExchangeAdapter
{
    private readonly PaperTradingAdapter _paper;
    private readonly OperationModeService _modeService;
    private IExchangeAdapter? _liveAdapter;
    private KrakenWebSocketAdapter? _wsAdapter;
    private KrakenRestAdapter? _restAdapter;

    public DelegatingExchangeAdapter(
        PaperTradingAdapter paper,
        OperationModeService modeService)
    {
        _paper = paper;
        _modeService = modeService;
    }

    /// <summary>Supplies the live exchange adapter once it is available.</summary>
    public void SetLiveAdapter(IExchangeAdapter live) => _liveAdapter = live;

    /// <summary>
    /// Supplies separate live adapters: the WebSocket adapter for market data
    /// and the REST adapter for orders and account operations.
    /// </summary>
    public void SetLiveAdapters(KrakenWebSocketAdapter wsAdapter, KrakenRestAdapter restAdapter)
    {
        _wsAdapter = wsAdapter;
        _restAdapter = restAdapter;
        // Keep _liveAdapter pointing to REST for non-market-data methods.
        _liveAdapter = restAdapter;
    }

    private bool IsLive =>
        _modeService.CurrentMode == "live" &&
        (_liveAdapter is not null || (_wsAdapter is not null && _restAdapter is not null));

    private IExchangeAdapter ActiveForMarketData =>
        IsLive && _wsAdapter is not null ? _wsAdapter : _paper;

    private IExchangeAdapter Active =>
        IsLive && _liveAdapter is not null ? _liveAdapter : _paper;

    // -------------------------------------------------------------------------
    // IExchangeAdapter delegation
    // -------------------------------------------------------------------------

    public bool IsRateLimited => Active.IsRateLimited;
    public DateTimeOffset? RateLimitResumesAt => Active.RateLimitResumesAt;

    public Task SubscribeToMarketDataAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken = default)
        => ActiveForMarketData.SubscribeToMarketDataAsync(assets, onSnapshot, cancellationToken);

    public Task<OrderAcknowledgement> PlaceOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
        => Active.PlaceOrderAsync(request, cancellationToken);

    public Task<CancellationResult> CancelOrderAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
        => Active.CancelOrderAsync(exchangeOrderId, cancellationToken);

    public Task<OrderStatusResult> GetOrderStatusAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
        => Active.GetOrderStatusAsync(exchangeOrderId, cancellationToken);

    public Task<AccountBalance> GetAccountBalanceAsync(
        CancellationToken cancellationToken = default)
        => Active.GetAccountBalanceAsync(cancellationToken);

    public Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(
        CancellationToken cancellationToken = default)
        => Active.GetOpenPositionsAsync(cancellationToken);

    public Task<IReadOnlyList<Trade>> GetTradeHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
        => Active.GetTradeHistoryAsync(since, cancellationToken);
}
