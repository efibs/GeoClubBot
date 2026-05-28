using FluentAssertions;
using UseCases.UseCases.Strikes;
using UseCases.UseCases.Strikes.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Strikes;

public sealed class AddStrikeCommandValidatorTests
{
    private readonly AddStrikeCommandValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForRecentStrikeDate()
    {
        var cmd = new AddStrikeCommand("Player1", DateTimeOffset.UtcNow.AddDays(-1));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenStrikeDateIsMoreThanOneHourInTheFuture()
    {
        var cmd = new AddStrikeCommand("Player1", DateTimeOffset.UtcNow.AddHours(2));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "StrikeDate");
    }

    [Fact]
    public void Validate_Allows_OneHourClockSkew()
    {
        // The validator allows up to +1h of clock drift; this is +30 minutes → still valid.
        var cmd = new AddStrikeCommand("Player1", DateTimeOffset.UtcNow.AddMinutes(30));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenNicknameIsEmpty()
    {
        var cmd = new AddStrikeCommand("", DateTimeOffset.UtcNow);

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MemberNickname");
    }
}
