using FluentAssertions;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.Excuses.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Excuses;

public sealed class UpdateExcuseCommandValidatorTests
{
    private readonly UpdateExcuseCommandValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForWellFormedCommand()
    {
        var cmd = new UpdateExcuseCommand(
            ExcuseId: Guid.NewGuid(),
            From: DateTimeOffset.UtcNow.AddDays(-1),
            To: DateTimeOffset.UtcNow.AddDays(7));

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenExcuseIdIsEmpty()
    {
        var cmd = new UpdateExcuseCommand(Guid.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ExcuseId");
    }

    [Fact]
    public void Validate_Fails_WhenFromIsNotBeforeTo()
    {
        var now = DateTimeOffset.UtcNow;
        var cmd = new UpdateExcuseCommand(Guid.NewGuid(), From: now.AddDays(1), To: now);

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("before its end"));
    }

    [Fact]
    public void Validate_Fails_WhenFromEqualsTo()
    {
        var now = DateTimeOffset.UtcNow;
        var cmd = new UpdateExcuseCommand(Guid.NewGuid(), now, now);

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Fails_WhenFromIsMoreThanFiveYearsInThePast()
    {
        var cmd = new UpdateExcuseCommand(
            Guid.NewGuid(),
            From: DateTimeOffset.UtcNow.AddYears(-6),
            To: DateTimeOffset.UtcNow.AddYears(-6).AddDays(1));

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "From");
    }
}
