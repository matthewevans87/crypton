using FluentValidation;

namespace AgentRunner.Tools;

public class PortfolioSummaryResponseValidator : AbstractValidator<PortfolioSummaryResponse>
{
    public PortfolioSummaryResponseValidator()
    {
        RuleFor(x => x.AvailableCapital)
            .NotNull()
            .WithMessage("availableCapital is required — Execution Service response is incomplete");

        RuleFor(x => x.Positions)
            .NotNull()
            .WithMessage("positions is required — Execution Service response is incomplete");
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
