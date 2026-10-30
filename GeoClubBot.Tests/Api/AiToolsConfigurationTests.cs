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

        // So the paragraph never tells of a tool that is not declared: a model told of a tool it cannot
        // call tries to call it in prose.
        AnswerTools.SystemPrompt.Should().NotContainAny(AnswerTools.Names);
    }

    [Fact]
    public void ToolsParagraph_HasTheModelSearchBeforeFallingBackOnItsOwnKnowledge()
    {
        // The main prompt says to complete an answer from the model's own knowledge when the guides fall
        // short. Unordered, "snow bollards" got "the guides don't cover this" and no search for "snow poles".
        Paragraph().Should().Contain("Search before you say the guides don't cover something or answer from your own knowledge.");
    }

    [Fact]
    public void ToolsParagraph_NamesAPartialAnswerAsTheReasonToCall()
    {
        // 2026-10-29: tools in 15 of 49 answers, species_range never, read_guide not for "the second guide
        // definitely mentions other plants"; "Don't call a tool when the excerpts already answer the
        // question" let excerpts answering part of it count as enough. And every three-round answer made
        // one search a round.
        Paragraph().Should().Contain("Call tools when the excerpts answer only part of the question")
            .And.Contain("You can make several calls at once: search for every place or term you need in one round.")
            .And.Contain("Don't call a tool when the excerpts fully answer the question.")
            .And.NotContain("already answer");
    }

    [Fact]
    public void ToolsParagraph_HasTheModelSearchTheAskedThingFirst_WhenTheExcerptsDescribeAnother()
    {
        // "I asked about snake traps. Not possum guards" was answered from the possum-guard excerpt, no
        // search made. The main prompt says the two are not the same; the tools say to look first.
        Paragraph().Should().Contain(
            "search the user's own words first; only if nothing turns up, say that the guides cover the one, not the other.");
    }

    [Fact]
    public void ToolsParagraph_StaysShort_LeavingEachToolsConditionsToItsDescription()
    {
        // Free models follow short prompts better, and a condition said in the paragraph and again in the
        // tool's description is sent twice with every question. It was 116 words before the 2026-10-29
        // replay, and grew to 185 by repeating the descriptions.
        Paragraph().Split(' ').Should().HaveCountLessThan(110);

        var descriptions = string.Join(' ', AnswerTools.Definitions(speciesRange: true).Select(tool => tool.Description));
        descriptions.Should().Contain("it asks for every, all or exactly which places and they name only some")
            .And.Contain("a list of regions or plants cut short, or the user says the guide says more")
            .And.Contain("guides rarely list every country, so excerpts naming some are a reason to call it");
        foreach (var condition in new[] { "every, all or exactly which places", "cut short", "rarely list every country" })
        {
            Paragraph().Should().NotContain(condition, "each tool's conditions are said once, in its description");
        }
    }

    /// <summary>The tools paragraph as one line, so a phrase can be found across its line breaks.</summary>
    private static string Paragraph() =>
        string.Join(' ', AnswerTools.SystemPrompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Defaults_StopAfterTwoToolRounds()
    {
        // No third round contributed a cited excerpt in the 2026-10-29 replay (11 answers took one).
        new AiToolsConfiguration().MaxToolRounds.Should().Be(2);
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
