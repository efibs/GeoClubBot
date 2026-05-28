using Constants;
using FluentValidation;

namespace UseCases.UseCases.GeoGuessrAccountLinking.Validators;

public sealed class StartAccountLinkingCommandValidator : AbstractValidator<StartAccountLinkingCommand>
{
    public StartAccountLinkingCommandValidator()
    {
        RuleFor(x => x.DiscordUserId)
            .GreaterThan(0ul).WithMessage("Discord user id must be greater than zero.");

        RuleFor(x => x.GeoGuessrUserId)
            .NotEmpty().WithMessage("GeoGuessr user id must not be empty.")
            .Length(StringLengthConstants.GeoGuessrUserIdLength)
            .WithMessage($"GeoGuessr user id must be exactly {StringLengthConstants.GeoGuessrUserIdLength} characters.");
    }
}
