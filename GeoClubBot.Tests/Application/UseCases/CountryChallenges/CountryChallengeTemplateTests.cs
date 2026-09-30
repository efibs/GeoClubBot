using FluentAssertions;
using UseCases.UseCases.CountryChallenges.Rendering;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

public sealed class CountryChallengeTemplateTests
{
    private static readonly DateOnly ChristmasEve = new(2026, 12, 24);

    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Mongolia Monday",
        ["country"] = "Mongolia"
    };

    [Fact]
    public void Render_ReplacesPlaceholders_IgnoringTheirCase()
    {
        CountryChallengeTemplate.Render("{{name}}: {{COUNTRY}}", Values, ChristmasEve)
            .Should().Be("Mongolia Monday: Mongolia");
    }

    [Theory]
    [InlineData("{{date}}", "2026-12-24")]
    [InlineData("{{date:dd.MM.}}", "24.12.")]
    [InlineData("{{date:dddd, d MMMM}}", "Thursday, 24 December")]
    public void Render_FormatsTheDateInvariantly(string template, string expected)
    {
        CountryChallengeTemplate.Render(template, Values, ChristmasEve).Should().Be(expected);
    }

    [Fact]
    public void Render_NeverExpandsPlaceholdersInsideAnInsertedValue()
    {
        var values = new Dictionary<string, string> { ["name"] = "{{country}}" };

        CountryChallengeTemplate.Render("{{name}}", values, ChristmasEve).Should().Be("{{country}}");
    }

    [Fact]
    public void FindProblems_AcceptsATemplateThatOnlyUsesItsPlaceholders()
    {
        CountryChallengeTemplate.FindProblems(
                "### {{flag}} {{Name}}\n{{country}} · {{settings}} · {{date:dd.MM.}}\n{{link}}",
                CountryChallengeTemplateKind.AnnouncementEntry)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindProblems_NamesAPlaceholderThatIsNotAvailable_AndListsTheOnesThatAre()
    {
        var problems = CountryChallengeTemplate.FindProblems("{{ranking}}", CountryChallengeTemplateKind.ThreadName).ToList();

        problems.Should().ContainSingle().Which.Should().Be(
            "{{ranking}} is not available here. Available: {{date}}, {{day}}, {{names}}.");
    }

    [Fact]
    public void FindProblems_RejectsAFormatOnAnythingButTheDate_AndAnInvalidDateFormat()
    {
        var problems = CountryChallengeTemplate
            .FindProblems("{{name:upper}} {{date:HH:mm}}", CountryChallengeTemplateKind.AnnouncementEntry)
            .ToList();

        problems.Should().HaveCount(2);
        problems[0].Should().Be("{{name}} does not take a format; only {{date}} does.");
        problems[1].Should().Contain("'HH:mm' is not a valid date format");
    }

    [Theory]
    [InlineData("{{ name }}")]
    [InlineData("{{name}")]
    public void FindProblems_CatchesBracesThatLookLikeAPlaceholderButAreNot(string template)
    {
        CountryChallengeTemplate.FindProblems(template, CountryChallengeTemplateKind.AnnouncementEntry)
            .Should().ContainSingle(p => p.Contains("'{{' that is not a placeholder"));
    }

    [Fact]
    public void ExtractMentions_AllowsExactlyThePingsWrittenIntoTheTemplates()
    {
        var mentions = CountryChallengeTemplate.ExtractMentions(
            ["<@&111> {{mentions}} <@222> <@!333>", "@here {{challenges}}"],
            [444]);

        mentions.RoleIds.Should().BeEquivalentTo([111UL, 444UL]);
        mentions.UserIds.Should().BeEquivalentTo([222UL, 333UL]);
        mentions.Everyone.Should().BeTrue();
    }

    [Fact]
    public void ExtractMentions_AllowsNothing_WhenTheTemplatesPingNoOne()
    {
        var mentions = CountryChallengeTemplate.ExtractMentions(["{{name}} someone@everyone.example"], []);

        mentions.IsEmpty.Should().BeTrue("an e-mail address is not @everyone");
    }
}
