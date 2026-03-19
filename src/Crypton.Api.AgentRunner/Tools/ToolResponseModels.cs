namespace AgentRunner.Tools;

/// <summary>
/// Typed response model for the Execution Service /portfolio/summary endpoint.
/// Required fields are nullable so a missing field deserializes to null (detectable by validator).
/// </summary>
public record PortfolioSummaryResponse
{
    public decimal TotalValue { get; init; }
    public decimal UnrealizedPnL { get; init; }
    public decimal? AvailableCapital { get; init; }
    public IReadOnlyList<PositionResponse>? Positions { get; init; }
    public IReadOnlyList<BalanceResponse> Balances { get; init; } = [];
    public DateTime LastUpdated { get; init; }
}

public record PositionResponse
{
    public string Id { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public decimal EntryPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnrealizedPnL { get; init; }
    public decimal UnrealizedPnLPercent { get; init; }
    public DateTime OpenedAt { get; init; }
}

public record BalanceResponse
{
    public string Asset { get; init; } = string.Empty;
    public decimal Available { get; init; }
    public decimal Hold { get; init; }
    public decimal Total { get; init; }
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
