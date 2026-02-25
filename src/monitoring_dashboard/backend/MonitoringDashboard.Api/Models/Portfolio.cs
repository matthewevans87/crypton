namespace MonitoringDashboard.Models;

public class PortfolioSummary
{
    public decimal TotalValue { get; set; }
    public decimal Change24h { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal AvailableCapital { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class Holding
{
    public string Asset { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Value => Quantity * CurrentPrice;
    public decimal AllocationPercent { get; set; }
}

public class Position
{
    public string Id { get; set; } = string.Empty;
    public string Asset { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty; // "long" or "short"
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Size { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal UnrealizedPnLPercent { get; set; }
    public DateTime OpenedAt { get; set; }
    public TimeSpan TimeInPosition => DateTime.UtcNow - OpenedAt;
    public bool IsNearStopLoss => StopLoss.HasValue && Direction == "long" && CurrentPrice <= StopLoss.Value * 1.05m
                                  || StopLoss.HasValue && Direction == "short" && CurrentPrice >= StopLoss.Value * 0.95m;
    public bool IsNearTakeProfit => TakeProfit.HasValue && Direction == "long" && CurrentPrice >= TakeProfit.Value * 0.95m
                                    || TakeProfit.HasValue && Direction == "short" && CurrentPrice <= TakeProfit.Value * 1.05m;
}

public class Trade
{
    public string Id { get; set; } = string.Empty;
    public string Asset { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Size { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public string ExitReason { get; set; } = string.Empty;
    public bool IsWin => PnL > 0;
}
