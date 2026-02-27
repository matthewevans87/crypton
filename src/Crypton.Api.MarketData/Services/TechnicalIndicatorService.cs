using MarketDataService.Models;

using MarketDataService.Adapters;
using MarketDataService.Models;

namespace MarketDataService.Services;

public interface ITechnicalIndicatorService
{
    Task<TechnicalIndicator?> CalculateAsync(string symbol, string timeframe, CancellationToken cancellationToken = default);
}

public class TechnicalIndicatorService : ITechnicalIndicatorService
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;

    public TechnicalIndicatorService(IExchangeAdapter exchangeAdapter, IMarketDataCache cache)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
    }

    public async Task<TechnicalIndicator?> CalculateAsync(string symbol, string timeframe, CancellationToken cancellationToken = default)
    {
        var cached = _cache.GetTechnicalIndicator(symbol, timeframe);
        if (cached != null)
        {
            return cached;
        }

        var ohlcv = await _exchangeAdapter.GetOhlcvAsync(symbol, timeframe, 100, cancellationToken);
        
        if (ohlcv == null || ohlcv.Count < 26)
        {
            return null;
        }

        var closes = ohlcv.Select(x => x.Close).ToList();
        
        var indicator = new TechnicalIndicator
        {
            Symbol = symbol,
            Timeframe = timeframe,
            LastUpdated = DateTime.UtcNow
        };

        indicator.Rsi = CalculateRsi(closes, 14);
        
        var (macd, signal, histogram) = CalculateMacd(closes, 12, 26, 9);
        indicator.Macd = macd;
        indicator.MacdSignal = signal;
        indicator.MacdHistogram = histogram;
        
        var (upper, middle, lower) = CalculateBollingerBands(closes, 20, 2);
        indicator.BollingerUpper = upper;
        indicator.BollingerMiddle = middle;
        indicator.BollingerLower = lower;

        if (indicator.Rsi.HasValue)
        {
            if (indicator.Rsi.Value > 70)
                indicator.Signal = "overbought";
            else if (indicator.Rsi.Value < 30)
                indicator.Signal = "oversold";
            else
                indicator.Signal = "neutral";
        }

        _cache.SetTechnicalIndicator(indicator);
        
        return indicator;
    }

    private decimal? CalculateRsi(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1)
            return null;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        if (gains.Count < period)
            return null;

        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        for (int i = period; i < gains.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
        }

        if (avgLoss == 0)
            return 100;

        var rs = avgGain / avgLoss;
        var rsi = 100 - (100 / (1 + rs));
        
        return Math.Round(rsi, 2);
    }

    private (decimal macd, decimal signal, decimal histogram) CalculateMacd(List<decimal> closes, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        if (closes.Count < slowPeriod + signalPeriod)
            return (0, 0, 0);

        var fastEma = CalculateEma(closes, fastPeriod);
        var slowEma = CalculateEma(closes, slowPeriod);
        
        var macdLine = fastEma - slowEma;
        
        var macdValues = new List<decimal>();
        for (int i = slowPeriod - 1; i < closes.Count; i++)
        {
            var fast = CalculateEma(closes.Take(i + 1).ToList(), fastPeriod);
            var slow = CalculateEma(closes.Take(i + 1).ToList(), slowPeriod);
            macdValues.Add(fast - slow);
        }

        var signalLine = CalculateEma(macdValues, signalPeriod);
        var histogram = macdLine - signalLine;
        
        return (Math.Round(macdLine, 2), Math.Round(signalLine, 2), Math.Round(histogram, 2));
    }

    private decimal CalculateEma(List<decimal> values, int period)
    {
        if (values.Count < period)
            return values.LastOrDefault();
            
        var multiplier = 2m / (period + 1);
        var ema = values.Take(period).Average();
        
        for (int i = period; i < values.Count; i++)
        {
            ema = (values[i] - ema) * multiplier + ema;
        }
        
        return ema;
    }

    private (decimal upper, decimal middle, decimal lower) CalculateBollingerBands(List<decimal> closes, int period, decimal standardDeviations)
    {
        if (closes.Count < period)
            return (0, 0, 0);

        var recentCloses = closes.TakeLast(period).ToList();
        var middle = recentCloses.Average();
        
        var variance = recentCloses.Sum(x => (x - middle) * (x - middle)) / period;
        var stdDev = (decimal)Math.Sqrt((double)variance);
        
        var upper = middle + (stdDev * standardDeviations);
        var lower = middle - (stdDev * standardDeviations);
        
        return (Math.Round(upper, 2), Math.Round(middle, 2), Math.Round(lower, 2));
    }
}
