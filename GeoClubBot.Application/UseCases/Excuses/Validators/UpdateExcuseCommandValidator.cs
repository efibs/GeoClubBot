using FluentValidation;

namespace UseCases.UseCases.Excuses.Validators;

public sealed class UpdateExcuseCommandValidator : AbstractValidator<UpdateExcuseCommand>
{
    public UpdateExcuseCommandValidator()
    {
        RuleFor(x => x.ExcuseId)
            .NotEqual(Guid.Empty).WithMessage("Excuse id must not be empty.");

        RuleFor(x => x.From)
            .GreaterThan(DateTimeOffset.UtcNow.AddYears(-5))
            .WithMessage("Excuse start must be within the last 5 years.");

        RuleFor(x => x)
            .Must(x => x.From < x.To)
            .WithMessage("Excuse start must be before its end.");
    }
}
