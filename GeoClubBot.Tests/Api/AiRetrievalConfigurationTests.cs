using System.ComponentModel.DataAnnotations;
using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoClubBot.Tests.Api;

// In Api/ rather than a Configuration/ folder: a GeoClubBot.Tests.Configuration namespace would
// shadow the Configuration project's namespace for the whole test assembly.
public sealed class AiRetrievalConfigurationTests
{
    [Fact]
    public void Defaults_AreValid() =>
        Validate(new AiRetrievalConfiguration()).Should().BeEmpty();

    [Fact]
    public void QueryPlanner_ShipsOff()
    {
        // A planned question costs three requests instead of two: on the 45 a day the bot ships with,
        // planning to its share of the allowance leaves 17 answers a day instead of 22.
        new AiRetrievalConfiguration().QueryPlanner.Enabled.Should().BeFalse();
        ShippedSettings().QueryPlanner.Enabled.Should().BeFalse();
    }

    [Fact]
    public void SettingsFile_RepeatsTheDefaults()
    {
        // The retrieval probe replays "shipped" as the defaults; a value changed in one place only
        // would have it measure something the bot does not do.
        ShippedSettings().Should().BeEquivalentTo(new AiRetrievalConfiguration());
    }

    [Fact]
    public void Validate_ChecksTheNestedSections_AndNamesTheirPaths()
    {
        var settings = new AiRetrievalConfiguration();
        settings.QueryPlanner.TimeoutSeconds = 0;
        settings.CountryBoost.Weight = -1;

        Validate(settings).Should().BeEquivalentTo(["QueryPlanner:TimeoutSeconds", "CountryBoost:Weight"]);
    }

    [Fact]
    public void Validate_RefusesAnAliasForNoCountry()
    {
        var settings = new AiRetrievalConfiguration();
        settings.CountryBoost.Aliases["sabah"] = [];

        Validate(settings).Should().ContainSingle().Which.Should().Contain("sabah");
    }

    [Fact]
    public void Validate_RefusesAnExcerptLimitOutOfRange() =>
        Validate(new AiRetrievalConfiguration { ExcerptLimit = 0 }).Should().ContainSingle();

    [Fact]
    public void Options_BindTheAiRetrievalSection_AndFailAtStartUpWhenInvalid()
    {
        var services = new ServiceCollection();
        services.AddClubBotOptions(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Retrieval:ExcerptLimit"] = "10",
                ["AI:Retrieval:CountryBoost:Aliases:borneo:0"] = "Malaysia",
                ["AI:Retrieval:QueryPlanner:TimeoutSeconds"] = "500"
            })
            .Build());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AiRetrievalConfiguration>>();

        var act = () => options.Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*TimeoutSeconds*");
    }

    /// <summary>The <c>AI:Retrieval</c> section of the API's <c>appsettings.json</c>, found by walking up from the test binary.</summary>
    private static AiRetrievalConfiguration ShippedSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the solution root should be discoverable from the test run");

        var section = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory!.FullName, "GeoClubBot.API", "appsettings.json"))
            .Build()
            .GetSection(AiRetrievalConfiguration.SectionName);
        section.Exists().Should().BeTrue();

        var settings = new AiRetrievalConfiguration();
        section.Bind(settings);
        return settings;
    }

    private static IEnumerable<string> Validate(AiRetrievalConfiguration settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        return results.SelectMany(result => result.MemberNames.Any() ? result.MemberNames : [result.ErrorMessage ?? string.Empty]);
    }
}
