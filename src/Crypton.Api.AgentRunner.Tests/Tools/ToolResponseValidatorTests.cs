using AgentRunner.Tools;
using FluentValidation.TestHelper;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class PortfolioSummaryResponseValidatorTests
{
    private readonly PortfolioSummaryResponseValidator _validator = new();

    private static PortfolioSummaryResponse ValidResponse() => new()
    {
        Mode = "paper",
        Balance = new BalanceSummaryResponse { AvailableUsd = 10000m },
        OpenPositions = [],
        RecentTrades = []
    };

    [Fact]
    public void Validate_ValidResponse_NoErrors()
    {
        var result = _validator.TestValidate(ValidResponse());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullBalance_HasError()
    {
        var model = ValidResponse() with { Balance = null };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Balance)
              .WithErrorMessage("balance is required — Execution Service response is incomplete");
    }

    [Fact]
    public void Validate_NullAvailableUsd_HasError()
    {
        var model = ValidResponse() with { Balance = new BalanceSummaryResponse { AvailableUsd = null } };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Balance!.AvailableUsd)
              .WithErrorMessage("balance.availableUsd is required — Execution Service response is incomplete");
    }

    [Fact]
    public void Validate_NullOpenPositions_HasError()
    {
        var model = ValidResponse() with { OpenPositions = null };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.OpenPositions)
              .WithErrorMessage("openPositions is required — Execution Service response is incomplete");
    }

    [Fact]
    public void Validate_NullBalanceAndNullOpenPositions_HasMultipleErrors()
    {
        var model = ValidResponse() with { Balance = null, OpenPositions = null };

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Balance);
        result.ShouldHaveValidationErrorFor(x => x.OpenPositions);
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
