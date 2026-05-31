using FluentAssertions;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.Excuses.Validators;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Excuses;

public sealed class ReadRelevantExcusesQueryValidatorTests
{
    private readonly ReadRelevantExcuesesQueryQueryValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_WhenUpcomingExcusesNumDaysIsOne()
    {
        var result = _validator.Validate(new ReadRelevantExcusesQuery(1));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Succeeds_WhenUpcomingExcusesNumDaysIsLarge()
    {
        var result = _validator.Validate(new ReadRelevantExcusesQuery(90));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenUpcomingExcusesNumDaysIsZero()
    {
        var result = _validator.Validate(new ReadRelevantExcusesQuery(0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ReadRelevantExcusesQuery.UpcomingExcusesNumDays));
    }

    [Fact]
    public void Validate_Fails_WhenUpcomingExcusesNumDaysIsNegative()
    {
        var result = _validator.Validate(new ReadRelevantExcusesQuery(-5));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ReadRelevantExcusesQuery.UpcomingExcusesNumDays));
    }
}