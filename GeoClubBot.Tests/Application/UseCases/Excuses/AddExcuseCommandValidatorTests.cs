using FluentAssertions;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.Excuses.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Excuses;

public sealed class AddExcuseCommandValidatorTests
{
    private readonly AddExcuseCommandValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForWellFormedExcuse()
    {
        var cmd = new AddExcuseCommand(
            MemberNickname: "Player1",
            From: DateTimeOffset.UtcNow.AddDays(-1),
            To: DateTimeOffset.UtcNow.AddDays(7));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenFromIsNotBeforeTo()
    {
        var now = DateTimeOffset.UtcNow;
        var cmd = new AddExcuseCommand("Player1", From: now.AddDays(1), To: now);

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("before its end"));
    }

    [Fact]
    public void Validate_Fails_WhenNicknameIsEmpty()
    {
        var cmd = new AddExcuseCommand(
            MemberNickname: "",
            From: DateTimeOffset.UtcNow,
            To: DateTimeOffset.UtcNow.AddDays(1));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MemberNickname");
    }

    [Fact]
    public void Validate_Fails_WhenFromIsMoreThanFiveYearsInThePast()
    {
        var cmd = new AddExcuseCommand(
            MemberNickname: "Player1",
            From: DateTimeOffset.UtcNow.AddYears(-6),
            To: DateTimeOffset.UtcNow.AddYears(-6).AddDays(1));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "From");
    }
}
