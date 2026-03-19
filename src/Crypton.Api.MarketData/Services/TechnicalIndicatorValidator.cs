using FluentValidation;
using MarketDataService.Models;

namespace MarketDataService.Services;

public class TechnicalIndicatorValidator : AbstractValidator<TechnicalIndicator>
{
    public TechnicalIndicatorValidator()
    {
        RuleFor(x => x.Symbol)
            .NotEmpty()
            .WithMessage("Symbol is required");

        RuleFor(x => x.Timeframe)
            .NotEmpty()
            .WithMessage("Timeframe is required");

        RuleFor(x => x.CurrentPrice)
            .NotNull()
            .WithMessage("CurrentPrice is required — live price data is unavailable");

        When(x => x.CurrentPrice.HasValue, () =>
        {
            RuleFor(x => x.CurrentPrice!.Value)
                .GreaterThan(0)
                .WithMessage("CurrentPrice must be greater than 0");
        });
    }
}
