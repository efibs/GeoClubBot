using Constants;
using FluentAssertions;
using UseCases.UseCases.DailyMissionReminder;
using UseCases.UseCases.DailyMissionReminder.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionReminder;

public sealed class SetDailyMissionReminderCommandValidatorTests
{
    private readonly SetDailyMissionReminderCommandValidator _validator = new();

    private static SetDailyMissionReminderCommand Command(
        ulong discordUserId = 123UL,
        string? timeZoneId = null,
        string? customMessage = null) =>
        new(discordUserId, new TimeOnly(8, 0), timeZoneId, customMessage);

    [Fact]
    public void Validate_Succeeds_WithNoOptionalFields()
    {
        _validator.Validate(Command()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Succeeds_WithKnownTimeZone()
    {
        _validator.Validate(Command(timeZoneId: "UTC")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenDiscordUserIdIsZero()
    {
        var result = _validator.Validate(Command(discordUserId: 0UL));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DiscordUserId");
    }

    [Fact]
    public void Validate_Fails_ForUnknownTimeZone()
    {
        var result = _validator.Validate(Command(timeZoneId: "Not/A/RealZone"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TimeZoneId");
    }

    [Fact]
    public void Validate_SkipsTimeZoneRule_WhenTimeZoneIsWhitespace()
    {
        // The time-zone rules are gated behind a non-whitespace check.
        _validator.Validate(Command(timeZoneId: "   ")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenCustomMessageExceedsMaxLength()
    {
        var tooLong = new string('x', StringLengthConstants.DailyMissionReminderCustomMessageMaxLength + 1);

        var result = _validator.Validate(Command(customMessage: tooLong));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CustomMessage");
    }

    [Fact]
    public void Validate_Succeeds_WhenCustomMessageAtMaxLength()
    {
        var atLimit = new string('x', StringLengthConstants.DailyMissionReminderCustomMessageMaxLength);

        _validator.Validate(Command(customMessage: atLimit)).IsValid.Should().BeTrue();
    }
}
