using Configuration;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace UseCases.UseCases.ClubMemberActivity.Validators;

public sealed class GetActivityLastDaysQueryValidator : AbstractValidator<GetActivityLastDaysQuery>
{
    public GetActivityLastDaysQueryValidator(IOptions<ActivityViewsConfiguration> config)
    {
        var maxDaysBack = config.Value.MaxDaysBack;

        RuleFor(x => x.DaysBack)
            .InclusiveBetween(1, maxDaysBack)
            .WithMessage($"Days back must be between 1 and {maxDaysBack}.");
    }
}
