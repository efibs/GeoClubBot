using Constants;
using FluentValidation;

namespace UseCases.UseCases.DailyMissionReminder.Validators;

public sealed class SetDailyMissionReminderCommandValidator : AbstractValidator<SetDailyMissionReminderCommand>
{
    public SetDailyMissionReminderCommandValidator()
    {
        RuleFor(x => x.DiscordUserId)
            .GreaterThan(0ul).WithMessage("Discord user id must be greater than zero.");

        RuleFor(x => x.TimeZoneId)
            .Must(IsKnownTimeZone!)
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZoneId))
            .WithMessage("Time zone id is not recognised by the host system.");

        RuleFor(x => x.TimeZoneId!)
            .MaximumLength(StringLengthConstants.TimeZoneIdMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZoneId))
            .WithMessage($"Time zone id must be at most {StringLengthConstants.TimeZoneIdMaxLength} characters.");

        RuleFor(x => x.CustomMessage!)
            .MaximumLength(StringLengthConstants.DailyMissionReminderCustomMessageMaxLength)
            .When(x => !string.IsNullOrEmpty(x.CustomMessage))
            .WithMessage($"Custom message must be at most {StringLengthConstants.DailyMissionReminderCustomMessageMaxLength} characters.");
    }

    private static bool IsKnownTimeZone(string timeZoneId)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
