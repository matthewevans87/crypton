import { useEffect, useState, useMemo } from 'react';
import {
  ComposedChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
  Cell,
} from 'recharts';
import { useDashboardStore } from '../../store/dashboard';
import { api } from '../../services/api';
import type { Ohlcv } from '../../types';

interface PriceChartPanelProps {
  config?: Record<string, unknown>;
}

interface ChartDataPoint {
  time: number;
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  isUp: boolean;
}

interface PriceLevel {
  price: number;
  label: string;
  color: string;
  type: 'entry' | 'stopLoss' | 'takeProfit';
}

export function PriceChartPanel({ config }: PriceChartPanelProps) {
  const asset = (config?.asset as string) || 'BTC/USD';
  const timeframe = (config?.timeframe as string) || '1h';
  
  const { strategy } = useDashboardStore();
  const [ohlcvData, setOhlcvData] = useState<Ohlcv[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    
    const fetchData = async () => {
      setLoading(true);
      setError(null);
      try {
        const data = await api.market.ohlcv(asset, timeframe, 100) as Ohlcv[];
        if (mounted) {
          setOhlcvData(data);
        }
      } catch (err) {
        if (mounted) {
          setError(err instanceof Error ? err.message : 'Failed to load chart data');
        }
      } finally {
        if (mounted) {
          setLoading(false);
        }
      }
    };

    fetchData();
    
    const interval = setInterval(fetchData, 30000);
    return () => {
      mounted = false;
      clearInterval(interval);
    };
  }, [asset, timeframe]);

  const chartData: ChartDataPoint[] = useMemo(() => {
    return ohlcvData.map(candle => ({
      time: new Date(candle.timestamp).getTime(),
      date: new Date(candle.timestamp).toLocaleString('en-US', { 
        month: 'short', 
        day: 'numeric', 
        hour: '2-digit', 
        minute: '2-digit' 
      }),
      open: candle.open,
      high: candle.high,
      low: candle.low,
      close: candle.close,
      volume: candle.volume,
      isUp: candle.close >= candle.open,
    }));
  }, [ohlcvData]);

  const priceLevels: PriceLevel[] = useMemo(() => {
    const levels: PriceLevel[] = [];
    const latestPrice = chartData.length > 0 ? chartData[chartData.length - 1].close : 0;
    const positionRules = strategy.current?.positionRules || [];
    
    const assetRules = positionRules.filter(rule => 
      rule.asset === asset || rule.asset === asset.replace('/USD', '')
    );

    assetRules.forEach((rule, index) => {
      if (rule.entryCondition) {
        const price = parsePriceFromCondition(rule.entryCondition, latestPrice);
        if (price) {
          levels.push({
            price,
            label: `Entry ${index + 1}`,
            color: 'var(--color-info)',
            type: 'entry',
          });
        }
      }

      if (rule.stopLoss) {
        levels.push({
          price: rule.stopLoss,
          label: `Stop Loss ${index + 1}`,
          color: 'var(--color-loss)',
          type: 'stopLoss',
        });
      }

      if (rule.takeProfit) {
        levels.push({
          price: rule.takeProfit,
          label: `Take Profit ${index + 1}`,
          color: 'var(--color-profit)',
          type: 'takeProfit',
        });
      }

      if (rule.takeProfitTargets) {
        rule.takeProfitTargets.forEach((target, tpIndex) => {
          levels.push({
            price: target.price,
            label: `TP${tpIndex + 1} ${index + 1}`,
            color: 'var(--color-profit)',
            type: 'takeProfit',
          });
        });
      }
    });

    return levels;
  }, [chartData, strategy.current?.positionRules, asset]);

  const { minPrice, maxPrice } = useMemo(() => {
    if (chartData.length === 0) {
      return { minPrice: 0, maxPrice: 0 };
    }
    
    const lows = chartData.map(d => d.low);
    const highs = chartData.map(d => d.high);
    
    let min = Math.min(...lows);
    let max = Math.max(...highs);
    
    const levels = priceLevels.map(p => p.price).filter(p => p > 0);
    if (levels.length > 0) {
      min = Math.min(min, ...levels);
      max = Math.max(max, ...levels);
    }
    
    const padding = (max - min) * 0.1;
    min = min - padding;
    max = max + padding;
    
    return { minPrice: min, maxPrice: max, priceRange: max - min };
  }, [chartData, priceLevels]);

  const formatPrice = (value: number): string => {
    if (value < 1) return value.toFixed(4);
    if (value < 100) return value.toFixed(2);
    return value.toLocaleString('en-US', { maximumFractionDigits: 0 });
  };

  if (loading && chartData.length === 0) {
    return (
      <div style={{ 
        display: 'flex', 
        alignItems: 'center', 
        justifyContent: 'center', 
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: 'var(--font-size-sm)'
      }}>
        Loading chart...
      </div>
    );
  }

  if (error) {
    return (
      <div style={{ 
        display: 'flex', 
        alignItems: 'center', 
        justifyContent: 'center', 
        height: '100%',
        color: 'var(--color-loss)',
        fontSize: 'var(--font-size-sm)'
      }}>
        {error}
      </div>
    );
  }

  if (chartData.length === 0) {
    return (
      <div style={{ 
        display: 'flex', 
        alignItems: 'center', 
        justifyContent: 'center', 
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: 'var(--font-size-sm)'
      }}>
        No data available
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', gap: 'var(--space-2)' }}>
      {/* Header with asset and timeframe */}
      <div style={{ 
        display: 'flex', 
        justifyContent: 'space-between', 
        alignItems: 'center',
        flexShrink: 0 
      }}>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', fontWeight: 600 }}>
          {asset}
        </span>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          {timeframe}
        </span>
      </div>

      {/* Legend */}
      {priceLevels.length > 0 && (
        <div style={{ 
          display: 'flex', 
          gap: 'var(--space-3)', 
          flexWrap: 'wrap',
          flexShrink: 0 
        }}>
          <LegendItem color="var(--color-info)" label="Entry" />
          <LegendItem color="var(--color-loss)" label="Stop Loss" />
          <LegendItem color="var(--color-profit)" label="Take Profit" />
        </div>
      )}

      {/* Chart */}
      <div style={{ flex: 1, minHeight: 0 }}>
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={chartData} margin={{ top: 10, right: 30, left: 10, bottom: 10 }}>
            <XAxis 
              dataKey="date" 
              tick={{ fontSize: 10, fill: 'var(--text-tertiary)' }}
              tickLine={{ stroke: 'var(--border-default)' }}
              axisLine={{ stroke: 'var(--border-default)' }}
              interval="preserveStartEnd"
              minTickGap={50}
            />
            <YAxis 
              domain={[minPrice, maxPrice]}
              tick={{ fontSize: 10, fill: 'var(--text-tertiary)' }}
              tickLine={{ stroke: 'var(--border-default)' }}
              axisLine={{ stroke: 'var(--border-default)' }}
              tickFormatter={(value) => formatPrice(value)}
              width={70}
            />
            <Tooltip 
              contentStyle={{ 
                backgroundColor: 'var(--bg-panel)', 
                border: '1px solid var(--border-default)',
                borderRadius: '4px',
                fontSize: 'var(--font-size-xs)',
              }}
              labelStyle={{ color: 'var(--text-primary)', marginBottom: '4px' }}
              formatter={(value: number, name: string) => {
                if (name === 'volume') return [value.toLocaleString(), 'Volume'];
                return [formatPrice(value), name.charAt(0).toUpperCase() + name.slice(1)];
              }}
              labelFormatter={(label) => label}
            />
            {/* Candlestick as bar chart - body */}
            <Bar dataKey="high" stackId="candle" fill="transparent" />
            <Bar dataKey="low" stackId="candle" fill="transparent" />
            <Bar dataKey="close" stackId="candle-body" fill="transparent">
              {chartData.map((entry, index) => (
                <Cell 
                  key={`cell-${index}`} 
                  fill={entry.isUp ? 'var(--color-profit)' : 'var(--color-loss)'} 
                />
              ))}
            </Bar>
            {/* Wick lines using reference lines would need custom implementation */}
            
            {/* Price level reference lines */}
            {priceLevels.map((level, index) => (
              <ReferenceLine
                key={`level-${index}`}
                y={level.price}
                stroke={level.color}
                strokeDasharray="3 3"
                strokeWidth={1}
              />
            ))}
          </ComposedChart>
        </ResponsiveContainer>
      </div>

      {/* Current price indicator */}
      {chartData.length > 0 && (
        <div style={{ 
          display: 'flex', 
          justifyContent: 'space-between',
          fontFamily: 'var(--font-mono)',
          fontSize: 'var(--font-size-xs)',
          flexShrink: 0 
        }}>
          <span style={{ color: 'var(--text-secondary)' }}>
            Last: <span style={{ color: chartData[chartData.length - 1].isUp ? 'var(--color-profit)' : 'var(--color-loss)' }}>
              {formatPrice(chartData[chartData.length - 1].close)}
            </span>
          </span>
          <span style={{ color: 'var(--text-secondary)' }}>
            Vol: {chartData[chartData.length - 1].volume.toLocaleString()}
          </span>
        </div>
      )}
    </div>
  );
}

function LegendItem({ color, label }: { color: string; label: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
      <div style={{ width: 12, height: 2, backgroundColor: color }} />
      <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>{label}</span>
    </div>
  );
}

function parsePriceFromCondition(condition: string, _fallbackPrice: number): number | null {
  if (!condition) return null;
  
  const priceMatch = condition.match(/(?:price|entry)[<>=]+\s*\$?([\d,.]+)/i) ||
                    condition.match(/\$?([\d,.]+)/);
  
  if (priceMatch) {
    const price = parseFloat(priceMatch[1].replace(/,/g, ''));
    if (!isNaN(price) && price > 0) {
      return price;
    }
  }
  
  return null;
}
