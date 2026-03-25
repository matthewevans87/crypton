namespace AgentRunner.Tools;

/// <summary>
/// Typed response model for the Execution Service GET /portfolio/summary endpoint.
/// Shape mirrors the anonymous object returned by PortfolioController.GetSummary().
/// </summary>
public record PortfolioSummaryResponse
{
    public string? Mode { get; init; }
    public BalanceSummaryResponse? Balance { get; init; }
    public IReadOnlyList<OpenPositionResponse>? OpenPositions { get; init; }
    public IReadOnlyList<ClosedTradeResponse>? RecentTrades { get; init; }
}

public record BalanceSummaryResponse
{
    public decimal? AvailableUsd { get; init; }
    public IReadOnlyDictionary<string, decimal>? AssetBalances { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public record OpenPositionResponse
{
    public string Id { get; init; } = string.Empty;
    public string StrategyPositionId { get; init; } = string.Empty;
    public string StrategyId { get; init; } = string.Empty;
    public string Asset { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal AverageEntryPrice { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public decimal? TrailingStopPrice { get; init; }
}

public record ClosedTradeResponse
{
    public string Id { get; init; } = string.Empty;
    public string Asset { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public DateTimeOffset ClosedAt { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public decimal RealizedPnl { get; init; }
    public string StrategyId { get; init; } = string.Empty;
}

/// <summary>
/// Typed response model for the Market Data Service /api/indicators endpoint.
/// </summary>
public record TechnicalIndicatorsResponse
{
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public decimal? CurrentPrice { get; init; }
    public decimal? High24h { get; init; }
    public decimal? Low24h { get; init; }
    public decimal? Volume24h { get; init; }
    public decimal? Rsi { get; init; }
    public decimal? Macd { get; init; }
    public decimal? MacdSignal { get; init; }
    public decimal? MacdHistogram { get; init; }
    public decimal? BollingerUpper { get; init; }
    public decimal? BollingerMiddle { get; init; }
    public decimal? BollingerLower { get; init; }
    public string? Signal { get; init; }
    public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Typed response model for the Market Data Service /api/prices endpoint.
/// </summary>
public record PriceTickerResponse
{
    public string? Asset { get; init; }
    public decimal? Price { get; init; }
    public decimal? Change24h { get; init; }
    public decimal? ChangePercent24h { get; init; }
    public decimal? Bid { get; init; }
    public decimal? Ask { get; init; }
    public decimal? High24h { get; init; }
    public decimal? Low24h { get; init; }
    public decimal? Volume24h { get; init; }
    public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Typed response model for the Market Data Service /api/macro endpoint.
/// </summary>
public record MacroSignalsResponse
{
    public string? Trend { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal? FearGreedIndex { get; init; }
    public string? Sentiment { get; init; }
    public decimal? BtcDominance { get; init; }
    public decimal? TotalMarketCap { get; init; }
    public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Typed response model for the Market Data Service /api/orderbook endpoint.
/// </summary>
public record OrderBookResponse
{
    public string? Symbol { get; init; }
    public IReadOnlyList<OrderBookEntryResponse>? Bids { get; init; }
    public IReadOnlyList<OrderBookEntryResponse>? Asks { get; init; }
    public DateTime LastUpdated { get; init; }
}

public record OrderBookEntryResponse
{
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
}
