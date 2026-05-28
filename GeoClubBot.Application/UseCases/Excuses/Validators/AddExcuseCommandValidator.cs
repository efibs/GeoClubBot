using FluentValidation;

namespace UseCases.UseCases.Excuses.Validators;

public sealed class AddExcuseCommandValidator : AbstractValidator<AddExcuseCommand>
{
    public AddExcuseCommandValidator()
    {
        RuleFor(x => x.MemberNickname)
            .NotEmpty().WithMessage("Member nickname must not be empty.");

        RuleFor(x => x.From)
            .GreaterThan(DateTimeOffset.UtcNow.AddYears(-5))
            .WithMessage("Excuse start must be within the last 5 years.");

        RuleFor(x => x)
            .Must(x => x.From < x.To)
            .WithMessage("Excuse start must be before its end.");
    }
}
