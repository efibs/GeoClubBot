using FluentValidation;

namespace UseCases.UseCases.ClubMemberActivity.Validators;

public sealed class GetActivityLastDaysQueryValidator : AbstractValidator<GetActivityLastDaysQuery>
{
    public GetActivityLastDaysQueryValidator()
    {
        RuleFor(x => x.DaysBack)
            .InclusiveBetween(1, 14)
            .WithMessage("Days back must be between 1 and 14.");
    }
}
