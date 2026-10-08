using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Xunit;

namespace GeoClubBot.Tests.Api;

// In Api/ rather than a Configuration/ folder: a GeoClubBot.Tests.Configuration namespace would
// shadow the Configuration project's namespace for the whole test assembly.
public sealed class WebsiteConfigurationValidatorTests
{
    private static readonly Guid MainClubId = Guid.NewGuid();
    private static readonly Guid SecondClubId = Guid.NewGuid();

    private static WebsiteConfigurationValidator CreateValidator() => new(
        new GeoGuessrConfigurationBuilder()
            .WithClub(MainClubId)
            .WithClub(SecondClubId, isMain: false)
            .BuildOptions());

    [Fact]
    public void Validate_Passes_WhenDisabledWithoutASecondClub()
    {
        var result = CreateValidator().Validate(null, new WebsiteConfiguration { Enabled = false });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Passes_WhenTheSecondClubIsAnotherConfiguredClub()
    {
        var result = CreateValidator().Validate(null, new WebsiteConfiguration { Enabled = true, SecondClubId = SecondClubId });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenEnabledWithoutASecondClub()
    {
        var result = CreateValidator().Validate(null, new WebsiteConfiguration { Enabled = true });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Website:SecondClubId");
    }

    [Fact]
    public void Validate_Fails_WhenTheSecondClubIsNotConfigured()
    {
        var result = CreateValidator().Validate(null, new WebsiteConfiguration { Enabled = true, SecondClubId = Guid.NewGuid() });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("not one of the GeoGuessr:Clubs");
    }

    [Fact]
    public void Validate_Fails_WhenTheSecondClubIsTheMainClub()
    {
        var result = CreateValidator().Validate(null, new WebsiteConfiguration { Enabled = true, SecondClubId = MainClubId });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("is the main club");
    }
}
