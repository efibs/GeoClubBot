using FluentValidation;

namespace UseCases.UseCases.Excuses.Validators;

public sealed class ReadRelevantExcuesesQueryQueryValidator : AbstractValidator<ReadRelevantExcusesQuery>
{
    public ReadRelevantExcuesesQueryQueryValidator()
    {
        RuleFor(x => x.UpcomingExcusesNumDays)
            .GreaterThan(0);
    }
}
