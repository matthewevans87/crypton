using MarketDataService.Adapters;
using MarketDataService.Controllers;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketDataService.Tests;

/// <summary>
/// Tests for <see cref="MacroController"/> — the GET /api/macro endpoint introduced in 3.7.
/// Verifies trend classification, volatility regime, Fear &amp; Greed proxy,
/// BTC dominance calculation, cache-fall-through, and error responses.
/// </summary>
public class MacroControllerTests
{
    private readonly Mock<IExchangeAdapter> _adapter = new();
    private readonly InMemoryMarketDataCache _cache = new();
    private readonly MacroController _controller;

    public MacroControllerTests()
    {
        _controller = new MacroController(
            _adapter.Object, _cache,
            NullLogger<MacroController>.Instance);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PriceTicker BtcTicker(
        decimal price = 60_000m,
        decimal change24h = 3m,
        decimal high = 63_000m,
        decimal low = 57_000m) =>
        new() { Asset = "BTC/USD", Price = price, ChangePercent24h = change24h, High24h = high, Low24h = low };

    private static PriceTicker EthTicker(decimal price = 3_500m) =>
        new() { Asset = "ETH/USD", Price = price, ChangePercent24h = 1m, High24h = price * 1.05m, Low24h = price * 0.95m };

    private static PriceTicker SolTicker(decimal price = 150m) =>
        new() { Asset = "SOL/USD", Price = price, ChangePercent24h = 0.5m, High24h = price * 1.04m, Low24h = price * 0.96m };

    private static MacroSignals ExtractSignals(ActionResult<MacroSignals> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<MacroSignals>(ok.Value);
    }

    // ── Trend classification ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMacroSignals_ChangeAbove2Pct_ReturnsBullishTrend()
    {
        _cache.SetPrice(BtcTicker(change24h: 3m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("bullish", signals.Trend);
    }

    [Fact]
    public async Task GetMacroSignals_ChangeBelow2Pct_ReturnsBearishTrend()
    {
        _cache.SetPrice(BtcTicker(change24h: -3m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("bearish", signals.Trend);
    }

    [Fact]
    public async Task GetMacroSignals_ChangeWithin2PctBand_ReturnsNeutralTrend()
    {
        _cache.SetPrice(BtcTicker(change24h: 0.5m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("neutral", signals.Trend);
    }

    [Theory]
    [InlineData(2.1, "bullish")]
    [InlineData(-2.1, "bearish")]
    [InlineData(2.0, "neutral")]   // boundary — exactly 2% is not > 2, so neutral
    [InlineData(-2.0, "neutral")]  // boundary — exactly -2% is not < -2, so neutral
    [InlineData(0.0, "neutral")]
    public async Task GetMacroSignals_TrendBoundaryValues_Correct(double change, string expectedTrend)
    {
        _cache.SetPrice(BtcTicker(change24h: (decimal)change));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal(expectedTrend, signals.Trend);
    }

    // ── Volatility regime ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMacroSignals_DayRangeAbove8Pct_ReturnsHighVolatility()
    {
        // (high - low) / price = (66000 - 60000) / 60000 = 10% (> 8%)
        _cache.SetPrice(BtcTicker(price: 60_000m, high: 66_000m, low: 60_000m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("high", signals.VolatilityRegime);
    }

    [Fact]
    public async Task GetMacroSignals_DayRangeBelow3Pct_ReturnsLowVolatility()
    {
        // (60600 - 59400) / 60000 = 1200/60000 = 2% (< 3%)
        _cache.SetPrice(BtcTicker(price: 60_000m, high: 60_600m, low: 59_400m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("low", signals.VolatilityRegime);
    }

    [Fact]
    public async Task GetMacroSignals_DayRangeBetween3And8Pct_ReturnsNormalVolatility()
    {
        // (63300 - 57000) / 60000 = 6300/60000 = 10.5%... use a narrower range
        // Let's use 5%: (63000 - 60000) / 60000 = 5%
        _cache.SetPrice(BtcTicker(price: 60_000m, high: 63_000m, low: 60_000m, change24h: 1m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("normal", signals.VolatilityRegime);
    }

    // ── Fear & Greed proxy ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMacroSignals_FgiClampedTo100_WhenExtremelyBullish()
    {
        // rawFgi = 50 + 20*5 = 150 → clamped to 100
        _cache.SetPrice(BtcTicker(change24h: 20m, price: 60_000m, high: 63_000m, low: 57_000m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal(100m, signals.FearGreedIndex);
    }

    [Fact]
    public async Task GetMacroSignals_FgiClampedTo0_WhenExtremelyBearish()
    {
        // rawFgi = 50 + (-20)*5 = -50 → clamped to 0
        _cache.SetPrice(BtcTicker(change24h: -20m, price: 60_000m, high: 63_000m, low: 57_000m));
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal(0m, signals.FearGreedIndex);
    }

    [Fact]
    public async Task GetMacroSignals_HighVolatility_DecrementsFgiBy10()
    {
        // zero change24h → rawFgi = 50; high vol (10% range) → -10 → fgi = 40
        _cache.SetPrice(BtcTicker(change24h: 0m, price: 60_000m, high: 66_000m, low: 60_000m)); // 10% range
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal(40m, signals.FearGreedIndex);
    }

    [Fact]
    public async Task GetMacroSignals_LowVolatility_IncrementsFgiBy5()
    {
        // zero change24h → rawFgi = 50; low vol (2% range) → +5 → fgi = 55
        _cache.SetPrice(BtcTicker(change24h: 0m, price: 60_000m, high: 60_600m, low: 59_400m)); // 2% range
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal(55m, signals.FearGreedIndex);
    }

    [Theory]
    [InlineData(100, "extreme greed")]
    [InlineData(75, "extreme greed")]
    [InlineData(74, "greed")]
    [InlineData(55, "greed")]
    [InlineData(54, "neutral")]
    [InlineData(45, "neutral")]
    [InlineData(44, "fear")]
    [InlineData(25, "fear")]
    [InlineData(24, "extreme fear")]
    [InlineData(0, "extreme fear")]
    public async Task GetMacroSignals_SentimentBoundaries_Correct(int fgiTarget, string expectedSentiment)
    {
        // Construct a change24h that drives rawFgi to the target (with normal vol at 5% range → no vol adjustment)
        // rawFgi = 50 + change * 5 → change = (fgiTarget - 50) / 5
        // Use a 5% day range to stay in "normal" volatility (3–8%), so no vol adj to FGI
        decimal change = (decimal)(fgiTarget - 50) / 5m;
        // If clamping needed, cap at ±10 (but we use InlineData within clamp range here)
        _cache.SetPrice(BtcTicker(change24h: change, price: 60_000m, high: 63_000m, low: 60_000m)); // 5% range = normal vol
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal(expectedSentiment, signals.Sentiment);
    }

    // ── BTC dominance calculation ──────────────────────────────────────────────

    [Fact]
    public async Task GetMacroSignals_AllSymbolsInCache_BtcDominanceCalculated()
    {
        // BTC cap  = 60000 * 19.7M = 1,182,000M
        // ETH cap  =  3500 * 120M  =   420,000M
        // SOL cap  =   150 *  550M =    82,500M
        // tracked  = 1,684,500M → estimatedTotal = 1,684,500 / 0.72 ≈ 2,339,583M
        // btcDom   = 1,182,000 / 2,339,583 * 100 ≈ 50.5%
        _cache.SetPrice(BtcTicker(price: 60_000m, change24h: 1m, high: 63_000m, low: 57_000m));
        _cache.SetPrice(EthTicker(price: 3_500m));
        _cache.SetPrice(SolTicker(price: 150m));

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.NotNull(signals.BtcDominance);
        Assert.InRange(signals.BtcDominance!.Value, 45m, 60m);
        Assert.NotNull(signals.TotalMarketCap);
        Assert.True(signals.TotalMarketCap!.Value > 0);
    }

    [Fact]
    public async Task GetMacroSignals_MissingEthAndSol_BtcDominanceAndMarketCapAreNull()
    {
        // Only BTC cached; Kraken fetch fails → partial fallback with just BTC
        // ETH and SOL are null → no dominance calculation possible
        _cache.SetPrice(BtcTicker());
        _adapter.Setup(a => a.GetPricesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Kraken timeout"));

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Null(signals.BtcDominance);
        Assert.Null(signals.TotalMarketCap);
    }

    // ── Cache fall-through and error handling ─────────────────────────────────

    [Fact]
    public async Task GetMacroSignals_EmptyCache_FetchesFromKraken()
    {
        var fresh = new List<PriceTicker> { BtcTicker(), EthTicker(), SolTicker() };
        _adapter.Setup(a => a.GetPricesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fresh);

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.NotNull(signals);
        _adapter.Verify(a => a.GetPricesAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMacroSignals_AllCached_DoesNotCallKraken()
    {
        _cache.SetPrice(BtcTicker());
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        await _controller.GetMacroSignals();

        _adapter.Verify(a => a.GetPricesAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMacroSignals_KrakenFails_NoCachedData_Returns503()
    {
        _adapter.Setup(a => a.GetPricesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Kraken timeout"));

        var result = await _controller.GetMacroSignals();

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetMacroSignals_KrakenFails_ButBtcCached_ReturnsPartialResult()
    {
        // Only BTC in cache; Kraken fails; we should still get a result (no BTC dominance, but trend/FGI/volatility computed)
        _cache.SetPrice(BtcTicker(change24h: 2.5m));
        _adapter.Setup(a => a.GetPricesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Kraken timeout"));

        var signals = ExtractSignals(await _controller.GetMacroSignals());

        Assert.Equal("bullish", signals.Trend);
        Assert.Null(signals.BtcDominance); // ETH/SOL unavailable
    }

    [Fact]
    public async Task GetMacroSignals_BtcNotAvailable_EvenAfterFetch_Returns503()
    {
        // Kraken returns only ETH and SOL (no BTC — pathological case)
        var fresh = new List<PriceTicker> { EthTicker(), SolTicker() };
        _adapter.Setup(a => a.GetPricesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fresh);

        var result = await _controller.GetMacroSignals();

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    // ── LastUpdated field ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMacroSignals_LastUpdated_IsRecentUtcTimestamp()
    {
        _cache.SetPrice(BtcTicker());
        _cache.SetPrice(EthTicker());
        _cache.SetPrice(SolTicker());

        var before = DateTime.UtcNow.AddSeconds(-1);
        var signals = ExtractSignals(await _controller.GetMacroSignals());
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(signals.LastUpdated, before, after);
    }
}
