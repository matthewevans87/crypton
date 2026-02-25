namespace MarketDataService.Models;

public class PriceTicker
{
    public string Asset { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Change24h { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal High24h { get; set; }
    public decimal Low24h { get; set; }
    public decimal Volume24h { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class OrderBookEntry
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public int Count { get; set; }
}

public class OrderBook
{
    public string Symbol { get; set; } = string.Empty;
    public List<OrderBookEntry> Bids { get; set; } = new();
    public List<OrderBookEntry> Asks { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class Ohlcv
{
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public class Trade
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public string Side { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class Balance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Hold { get; set; }
    public decimal Total => Available + Hold;
}

public class Position
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal UnrealizedPnLPercent { get; set; }
    public DateTime OpenedAt { get; set; }
}

public class PortfolioSummary
{
    public decimal TotalValue { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal AvailableCapital { get; set; }
    public List<Balance> Balances { get; set; } = new();
    public List<Position> Positions { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class TechnicalIndicator
{
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public decimal? Rsi { get; set; }
    public decimal? Macd { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? MacdHistogram { get; set; }
    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerMiddle { get; set; }
    public decimal? BollingerLower { get; set; }
    public string? Signal { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class ExchangeStatus
{
    public string Exchange { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public int ReconnectCount { get; set; }
    public string? Error { get; set; }
}
