using Constants;
using FluentAssertions;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.GeoGuessrAccountLinking.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.GeoGuessrAccountLinking;

public sealed class CompleteAccountLinkingCommandValidatorTests
{
    private readonly CompleteAccountLinkingCommandValidator _validator = new();

    private static string ValidUserId() => new('a', StringLengthConstants.GeoGuessrUserIdLength);
    private static string ValidOtp() => new('x', StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);

    [Fact]
    public void Validate_Succeeds_ForWellFormedCommand()
    {
        var cmd = new CompleteAccountLinkingCommand(123UL, ValidUserId(), ValidOtp());

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenDiscordUserIdIsZero()
    {
        var cmd = new CompleteAccountLinkingCommand(0UL, ValidUserId(), ValidOtp());

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DiscordUserId");
    }

    [Fact]
    public void Validate_Fails_WhenGeoGuessrUserIdHasWrongLength()
    {
        var cmd = new CompleteAccountLinkingCommand(123UL, "tooShort", ValidOtp());

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "GeoGuessrUserId");
    }

    [Fact]
    public void Validate_Fails_WhenOneTimePasswordHasWrongLength()
    {
        var cmd = new CompleteAccountLinkingCommand(123UL, ValidUserId(), "short");

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "OneTimePassword");
    }
}
