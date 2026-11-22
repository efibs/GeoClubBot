using System.Text;
using Discord;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.RandomDraws;
using UseCases.UseCases.RandomDraws;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>The rendered <c>/random</c> results, snapshot-tested via Verify.</summary>
public sealed class RandomDrawFormatterTests
{
    private static readonly DrawableCountry France = new("FR", "France", "🇫🇷", new Uri("https://www.plonkit.net/france"));
    private static readonly DrawableCountry Afghanistan = new("AF", "Afghanistan", "🇦🇫", null);

    [Fact]
    public Task Render_OneCountry_IsAHeadingWithItsGuide() =>
        Verify(RenderEmbed(RandomDrawFormatter.Render(new DrawCard(
            "🌍 Random country",
            [RandomDrawFormatter.Country(France)],
            RandomDrawFormatter.Footer(1, 132, "countries with Street View", "Fibs")))!));

    [Fact]
    public Task Render_SeveralMembersOfAllClubs_AreNumberedWithTheirClubAndAccount() =>
        Verify(RenderEmbed(RandomDrawFormatter.Render(new DrawCard(
            "👥 Random members",
            [
                RandomDrawFormatter.Member(new DrawableClubMember("abc", "Plonk_er", 42, "Main Club"), withClub: true),
                RandomDrawFormatter.Member(new DrawableClubMember("def", "Geo", null, "Second Club"), withClub: true)
            ],
            RandomDrawFormatter.Footer(2, 2, "all our clubs", "Fibs"),
            RandomDrawFormatter.ShortPoolNote(5, 2)))!));

    [Fact]
    public Task Render_SeveralCountries_LinkOnlyThoseWithAGuide() =>
        Verify(RenderEmbed(RandomDrawFormatter.Render(new DrawCard(
            "🌍 Random countries",
            [RandomDrawFormatter.Country(France), RandomDrawFormatter.Country(Afghanistan)],
            RandomDrawFormatter.Footer(2, 249, "all countries", "Fibs")))!));

    [Fact]
    public Task Render_Teams_AreFieldsWithTheirSizes() =>
        Verify(RenderEmbed(RandomDrawFormatter.Render(new DrawCard(
            "🏁 Random teams",
            [],
            "3 into 2 teams · 🔊 General · drawn by Fibs",
            Teams: [new DrawTeam("Team 1", ["<@1>", "<@2>"]), new DrawTeam("Team 2", ["<@3>"])]))!));

    [Fact]
    public void Render_IsNull_WhenTheDrawIsTooLongForOneEmbed()
    {
        var items = Enumerable.Range(0, 100).Select(_ => new string('x', 100)).ToList();

        RandomDrawFormatter.Render(new DrawCard("🔀 Shuffled", items, "footer")).Should().BeNull();
    }

    [Fact]
    public void Member_EscapesMarkdownInNicknames()
    {
        RandomDrawFormatter.Member(new DrawableClubMember("id", "*bold*", null, "Club"), withClub: false)
            .Should().Be(@"[\*bold\*](https://www.geoguessr.com/user/id)");
    }

    [Fact]
    public void ShortPoolNote_IsOnlyForADrawWantingMoreThanThePool()
    {
        RandomDrawFormatter.ShortPoolNote(3, 3).Should().BeNull();
        RandomDrawFormatter.ShortPoolNote(4, 3).Should().Contain("Only 3");
    }

    private static string RenderEmbed(Embed embed)
    {
        var text = new StringBuilder()
            .AppendLine($"Title: {embed.Title}")
            .AppendLine("Description:")
            .AppendLine(embed.Description)
            .AppendLine();

        foreach (var field in embed.Fields)
        {
            text.AppendLine($"Field: {field.Name}").AppendLine(field.Value).AppendLine();
        }

        return text.AppendLine($"Footer: {embed.Footer?.Text}").ToString();
    }
}

public sealed class RandomPeopleTests
{
    private static DrawPerson Person(ulong id, bool bot = false, bool deaf = false, params ulong[] roles) =>
        new(id, bot, deaf, roles.ToHashSet());

    [Fact]
    public void Eligible_LeavesOutBotsTheDeafenedAndTheExcluded()
    {
        var people = new[] { Person(1), Person(2, bot: true), Person(3, deaf: true), Person(4), Person(1) };

        RandomPeople.Eligible(people, new HashSet<ulong> { 4 }).Should().Equal(1UL);
    }

    [Fact]
    public void WithRoles_MatchesAnyOrAll()
    {
        var people = new[] { Person(1, roles: [10]), Person(2, roles: [10, 20]), Person(3, roles: [30]) };

        RandomPeople.WithRoles(people, [10, 20], RoleMatch.Any).Select(p => p.Id).Should().Equal(1UL, 2UL);
        RandomPeople.WithRoles(people, [10, 20], RoleMatch.All).Select(p => p.Id).Should().Equal(2UL);
    }

    [Fact]
    public void Excluded_HoldsTheCallerOnlyWhenAsked()
    {
        RandomPeople.Excluded(7, excludeMe: false, excludedUserId: 8).Should().BeEquivalentTo([8UL]);
        RandomPeople.Excluded(7, excludeMe: true, excludedUserId: null).Should().BeEquivalentTo([7UL]);
    }

    [Fact]
    public void DrawClubSuggestions_LeadWithAllOurClubs()
    {
        var club = Club.Create(Guid.NewGuid(), "Speedrunners", 10);

        var suggestions = DrawClubAutocompleteHandler.BuildSuggestions([club], string.Empty).ToList();

        suggestions.Select(s => s.Value).Should().Equal(DrawClubAutocompleteHandler.AllClubsValue, club.ClubId.ToString());
        DrawClubAutocompleteHandler.BuildSuggestions([club], "speed").Should().ContainSingle();
    }
}
