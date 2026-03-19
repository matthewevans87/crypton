using AgentRunner.Tools;
using FluentValidation.TestHelper;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class PortfolioSummaryResponseValidatorTests
{
    private readonly PortfolioSummaryResponseValidator _validator = new();

    [Fact]
    public void Validate_ValidResponse_NoErrors()
    {
        var model = new PortfolioSummaryResponse
        {
            AvailableCapital = 10000m,
            Positions = [],
            TotalValue = 10000m
        };

        var result = _validator.TestValidate(model);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullAvailableCapital_HasError()
    {
        var model = new PortfolioSummaryResponse
        {
            AvailableCapital = null,
            Positions = []
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.AvailableCapital)
              .WithErrorMessage("availableCapital is required — Execution Service response is incomplete");
    }

    [Fact]
    public void Validate_NullPositions_HasError()
    {
        var model = new PortfolioSummaryResponse
        {
            AvailableCapital = 10000m,
            Positions = null
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Positions)
              .WithErrorMessage("positions is required — Execution Service response is incomplete");
    }

    [Fact]
    public void Validate_BothFieldsMissing_HasMultipleErrors()
    {
        var model = new PortfolioSummaryResponse
        {
            AvailableCapital = null,
            Positions = null
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.AvailableCapital);
        result.ShouldHaveValidationErrorFor(x => x.Positions);
    }
}

public class TechnicalIndicatorsResponseValidatorTests
{
    private readonly TechnicalIndicatorsResponseValidator _validator = new();

    [Fact]
    public void Validate_ValidResponse_NoErrors()
    {
        var model = new TechnicalIndicatorsResponse
        {
            Symbol = "BTC/USD",
            Timeframe = "1d",
            CurrentPrice = 65000m,
            Rsi = 55m
        };

        var result = _validator.TestValidate(model);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullCurrentPrice_HasError()
    {
        var model = new TechnicalIndicatorsResponse
        {
            Symbol = "BTC/USD",
            Timeframe = "1d",
            CurrentPrice = null
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.CurrentPrice)
              .WithErrorMessage("currentPrice is required — live price data is unavailable");
    }

    [Fact]
    public void Validate_ZeroCurrentPrice_HasError()
    {
        var model = new TechnicalIndicatorsResponse
        {
            Symbol = "BTC/USD",
            Timeframe = "1d",
            CurrentPrice = 0m
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.CurrentPrice!.Value);
    }

    [Fact]
    public void Validate_NegativeCurrentPrice_HasError()
    {
        var model = new TechnicalIndicatorsResponse
        {
            Symbol = "BTC/USD",
            Timeframe = "1d",
            CurrentPrice = -1m
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.CurrentPrice!.Value);
    }

    [Fact]
    public void Validate_EmptySymbol_HasError()
    {
        var model = new TechnicalIndicatorsResponse
        {
            Symbol = "",
            Timeframe = "1d",
            CurrentPrice = 65000m
        };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Symbol);
    }
}
