using System.ComponentModel.DataAnnotations;
using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.Api;

// In Api/ rather than a Configuration/ folder: a GeoClubBot.Tests.Configuration namespace would
// shadow the Configuration project's namespace for the whole test assembly.
public sealed class AiToolsConfigurationTests
{
    [Fact]
    public void Defaults_AreValid() =>
        Validate(new AiToolsConfiguration()).Should().BeEmpty();

    [Fact]
    public void SettingsFile_RepeatsTheDefaults() =>
        ShippedSettings().Should().BeEquivalentTo(new AiToolsConfiguration());

    [Fact]
    public void Validate_ChecksTheSpeciesSection_AndNamesItsPath()
    {
        var settings = new AiToolsConfiguration();
        settings.SpeciesRange.RequestTimeoutSeconds = 0;
        settings.SpeciesRange.BaseUrl = "api.gbif.org";

        Validate(settings).Should().BeEquivalentTo(
            ["SpeciesRange:RequestTimeoutSeconds", "SpeciesRange:BaseUrl must be an absolute URL."]);
    }

    [Theory]
    [InlineData(nameof(AiToolsConfiguration.MaxToolRounds), 0)]
    [InlineData(nameof(AiToolsConfiguration.MaxToolCallsPerRound), 9)]
    [InlineData(nameof(AiToolsConfiguration.AnswerDeadlineSeconds), 5)]
    public void Validate_RefusesALimitOutOfRange(string property, int value)
    {
        var settings = new AiToolsConfiguration();
        typeof(AiToolsConfiguration).GetProperty(property)!.SetValue(settings, value);

        Validate(settings).Should().Equal(property);
    }

    [Fact]
    public void Options_BindTheAiToolsSection_AndFailAtStartUpWhenInvalid()
    {
        var services = new ServiceCollection();
        services.AddClubBotOptions(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Tools:MaxToolRounds"] = "2",
                ["AI:Tools:SpeciesRange:MaxAreas"] = "0"
            })
            .Build());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AiToolsConfiguration>>();

        var act = () => options.Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxAreas*");
    }

    [Fact]
    public void ToolsAreDeclared_OnlyAsTheyAreTurnedOn()
    {
        AnswerTools.Definitions(speciesRange: true).Select(tool => tool.Name)
            .Should().Equal(AnswerTools.SearchGuides, AnswerTools.ReadGuide, AnswerTools.SpeciesRange);
        AnswerTools.Definitions(speciesRange: false).Select(tool => tool.Name).Should().NotContain(AnswerTools.SpeciesRange);
        AnswerTools.SystemPromptFor(speciesRange: false).Should().NotContain(AnswerTools.SpeciesRange,
            "a model told of a tool it cannot call tries to call it in prose");
        AnswerTools.SystemPromptFor(speciesRange: true).Should().Contain(AnswerTools.SpeciesRange);
    }

    [Fact]
    public void EveryToolSchema_IsAJsonObjectSchema_ThatNamesItsRequiredArguments()
    {
        foreach (var tool in AnswerTools.Definitions(speciesRange: true))
        {
            using var schema = System.Text.Json.JsonDocument.Parse(tool.ParametersSchemaJson);
            schema.RootElement.GetProperty("type").GetString().Should().Be("object", tool.Name);

            var properties = schema.RootElement.GetProperty("properties");
            foreach (var required in schema.RootElement.GetProperty("required").EnumerateArray())
            {
                properties.TryGetProperty(required.GetString()!, out _).Should().BeTrue($"{tool.Name} requires {required}");
            }

            tool.Description.Should().Contain("Use it", $"{tool.Name} must say when to call it");
        }
    }

    /// <summary>The <c>AI:Tools</c> section of the API's <c>appsettings.json</c>, found by walking up from the test binary.</summary>
    private static AiToolsConfiguration ShippedSettings()
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
            .GetSection(AiToolsConfiguration.SectionName);
        section.Exists().Should().BeTrue();

        var settings = new AiToolsConfiguration();
        section.Bind(settings);
        return settings;
    }

    private static IEnumerable<string> Validate(AiToolsConfiguration settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        return results.SelectMany(result => result.MemberNames.Any() ? result.MemberNames : [result.ErrorMessage ?? string.Empty]);
    }
}
