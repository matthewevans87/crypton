namespace MonitoringDashboard.Models;

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
    public DateTime LastUpdated { get; set; }
}

public class TechnicalIndicator
{
    public string Asset { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public decimal? Rsi { get; set; }
    public decimal? Macd { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? MacdHistogram { get; set; }
    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerMiddle { get; set; }
    public decimal? BollingerLower { get; set; }
    public string? Signal { get; set; } // "overbought", "oversold", "neutral"
    public DateTime LastUpdated { get; set; }
}

public class MacroSignals
{
    public string Trend { get; set; } = "neutral"; // "bullish", "bearish", "neutral"
    public string VolatilityRegime { get; set; } = "normal"; // "low", "normal", "high"
    public decimal? FearGreedIndex { get; set; }
    public string? Sentiment { get; set; }
    public decimal? BtcDominance { get; set; }
    public decimal? TotalMarketCap { get; set; }
    public DateTime LastUpdated { get; set; }
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
