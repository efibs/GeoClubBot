using FluentValidation;

namespace UseCases.UseCases.Strikes.Validators;

public sealed class AddStrikeCommandValidator : AbstractValidator<AddStrikeCommand>
{
    public AddStrikeCommandValidator()
    {
        RuleFor(x => x.MemberNickname)
            .NotEmpty().WithMessage("Member nickname must not be empty.");

        // Allow up to one hour of clock skew when admins backdate or "now" the strike.
        RuleFor(x => x.StrikeDate)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddHours(1))
            .WithMessage("Strike date must not be more than one hour in the future.");
    }
}
