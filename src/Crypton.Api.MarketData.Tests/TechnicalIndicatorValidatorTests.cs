using MarketDataService.Models;
using MarketDataService.Services;
using Xunit;

namespace MarketDataService.Tests;

public class TechnicalIndicatorValidatorTests
{
    private readonly TechnicalIndicatorValidator _validator = new();

    private static TechnicalIndicator ValidIndicator() => new()
    {
        Symbol = "BTC/USD",
        Timeframe = "1h",
        CurrentPrice = 50000m
    };

    [Fact]
    public void Validate_ValidIndicator_Passes()
    {
        var result = _validator.Validate(ValidIndicator());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NullCurrentPrice_Fails()
    {
        var indicator = ValidIndicator();
        indicator.CurrentPrice = null;
        var result = _validator.Validate(indicator);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TechnicalIndicator.CurrentPrice));
    }

    [Fact]
    public void Validate_ZeroCurrentPrice_Fails()
    {
        var indicator = ValidIndicator();
        indicator.CurrentPrice = 0m;
        var result = _validator.Validate(indicator);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "CurrentPrice.Value");
    }

    [Fact]
    public void Validate_NegativeCurrentPrice_Fails()
    {
        var indicator = ValidIndicator();
        indicator.CurrentPrice = -100m;
        var result = _validator.Validate(indicator);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "CurrentPrice.Value");
    }

    [Fact]
    public void Validate_EmptySymbol_Fails()
    {
        var indicator = ValidIndicator();
        indicator.Symbol = "";
        var result = _validator.Validate(indicator);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TechnicalIndicator.Symbol));
    }

    [Fact]
    public void Validate_EmptyTimeframe_Fails()
    {
        var indicator = ValidIndicator();
        indicator.Timeframe = "";
        var result = _validator.Validate(indicator);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TechnicalIndicator.Timeframe));
    }
}
