using FluentValidation;

namespace AgentRunner.Tools;

public class PortfolioSummaryResponseValidator : AbstractValidator<PortfolioSummaryResponse>
{
    public PortfolioSummaryResponseValidator()
    {
        RuleFor(x => x.Balance)
            .NotNull()
            .WithMessage("balance is required — Execution Service response is incomplete");

        When(x => x.Balance != null, () =>
        {
            RuleFor(x => x.Balance!.AvailableUsd)
                .NotNull()
                .WithMessage("balance.availableUsd is required — Execution Service response is incomplete");
        });

        RuleFor(x => x.OpenPositions)
            .NotNull()
            .WithMessage("openPositions is required — Execution Service response is incomplete");
    }
}

public class TechnicalIndicatorsResponseValidator : AbstractValidator<TechnicalIndicatorsResponse>
{
    public TechnicalIndicatorsResponseValidator()
    {
        RuleFor(x => x.Symbol)
            .NotEmpty()
            .WithMessage("symbol is required");

        RuleFor(x => x.CurrentPrice)
            .NotNull()
            .WithMessage("currentPrice is required — live price data is unavailable");

        When(x => x.CurrentPrice.HasValue, () =>
        {
            RuleFor(x => x.CurrentPrice!.Value)
                .GreaterThan(0)
                .WithMessage("currentPrice must be greater than 0");
        });
    }
}

public class PriceTickerResponseValidator : AbstractValidator<PriceTickerResponse>
{
    public PriceTickerResponseValidator()
    {
        RuleFor(x => x.Asset)
            .NotEmpty()
            .WithMessage("asset is required");

        RuleFor(x => x.Price)
            .NotNull()
            .WithMessage("price is required — live price data is unavailable");

        When(x => x.Price.HasValue, () =>
        {
            RuleFor(x => x.Price!.Value)
                .GreaterThan(0)
                .WithMessage("price must be greater than 0");
        });
    }
}

public class MacroSignalsResponseValidator : AbstractValidator<MacroSignalsResponse>
{
    public MacroSignalsResponseValidator()
    {
        RuleFor(x => x.Trend)
            .NotEmpty()
            .WithMessage("trend is required");

        RuleFor(x => x.VolatilityRegime)
            .NotEmpty()
            .WithMessage("volatilityRegime is required");
    }
}

public class OrderBookResponseValidator : AbstractValidator<OrderBookResponse>
{
    public OrderBookResponseValidator()
    {
        RuleFor(x => x.Symbol)
            .NotEmpty()
            .WithMessage("symbol is required");

        RuleFor(x => x.Bids)
            .NotNull()
            .WithMessage("bids are required");

        RuleFor(x => x.Asks)
            .NotNull()
            .WithMessage("asks are required");
    }
}
