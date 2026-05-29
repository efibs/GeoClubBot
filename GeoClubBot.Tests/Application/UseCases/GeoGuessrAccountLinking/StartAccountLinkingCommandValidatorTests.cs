using Constants;
using FluentAssertions;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.GeoGuessrAccountLinking.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.GeoGuessrAccountLinking;

public sealed class StartAccountLinkingCommandValidatorTests
{
    private readonly StartAccountLinkingCommandValidator _validator = new();

    private static string ValidUserId() => new('a', StringLengthConstants.GeoGuessrUserIdLength);

    [Fact]
    public void Validate_Succeeds_ForValidCommand()
    {
        var cmd = new StartAccountLinkingCommand(DiscordUserId: 123UL, GeoGuessrUserId: ValidUserId());

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenDiscordUserIdIsZero()
    {
        var cmd = new StartAccountLinkingCommand(0UL, ValidUserId());

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DiscordUserId");
    }

    [Fact]
    public void Validate_Fails_WhenGeoGuessrUserIdIsEmpty()
    {
        var cmd = new StartAccountLinkingCommand(123UL, "");

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "GeoGuessrUserId");
    }

    [Theory]
    [InlineData(StringLengthConstants.GeoGuessrUserIdLength - 1)]
    [InlineData(StringLengthConstants.GeoGuessrUserIdLength + 1)]
    public void Validate_Fails_WhenGeoGuessrUserIdHasWrongLength(int length)
    {
        var cmd = new StartAccountLinkingCommand(123UL, new string('a', length));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "GeoGuessrUserId");
    }
}
