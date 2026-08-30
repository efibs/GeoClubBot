using System.Text;
using Discord;
using GeoClubBot.Discord.InputAdapters.Interactions.Activity;
using UseCases.UseCases.InactiveMembers;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The whole rendered inactive-members embed is snapshot-tested via Verify — any wording, ordering
/// or mention-format change shows up as a reviewable diff in the committed <c>*.verified.txt</c>
/// files beside this test.
/// </summary>
public sealed class InactiveMembersFormatterTests
{
    // A fixed date keeps the snapshots deterministic.
    private static readonly DateOnly Day = new(2026, 7, 10);

    private static TodaysInactiveMembers BuildReport(
        int totalMembers,
        IReadOnlyList<InactiveMember>? missionInactive = null,
        IReadOnlyList<InactiveMember>? challengeInactive = null) =>
        new(
            ClubId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClubName: "Awesome Club",
            Day: Day,
            TotalMembers: totalMembers,
            MissionInactive: missionInactive ?? [],
            ChallengeInactive: challengeInactive ?? []);

    [Fact]
    public Task BuildEmbed_ListsInactiveMembers_WithAndWithoutLinkedDiscordAccounts()
    {
        var report = BuildReport(
            totalMembers: 5,
            missionInactive:
            [
                new InactiveMember("Alpha", DiscordUserId: 111111111111111111UL),
                new InactiveMember("Bravo", DiscordUserId: null),
                new InactiveMember("Charlie", DiscordUserId: 222222222222222222UL)
            ],
            challengeInactive:
            [
                new InactiveMember("Bravo", DiscordUserId: null),
                new InactiveMember("Delta", DiscordUserId: 333333333333333333UL)
            ]);

        return Verify(RenderEmbed(InactiveMembersFormatter.BuildEmbed(report).Build()));
    }

    [Fact]
    public Task BuildEmbed_WithOnlyOneAwardOutstanding_StillShowsBothSections()
    {
        // Everyone did the daily mission but two members skipped the daily challenge - the whole
        // point of splitting the report in two.
        var report = BuildReport(
            totalMembers: 4,
            challengeInactive:
            [
                new InactiveMember("Alpha", DiscordUserId: 111111111111111111UL),
                new InactiveMember("Bravo", DiscordUserId: null)
            ]);

        return Verify(RenderEmbed(InactiveMembersFormatter.BuildEmbed(report).Build()));
    }

    [Fact]
    public Task BuildEmbed_WithNoInactiveMembers_CelebratesFullCompletion()
    {
        var report = BuildReport(totalMembers: 3);

        return Verify(RenderEmbed(InactiveMembersFormatter.BuildEmbed(report).Build()));
    }

    /// <summary>Flattens an embed into plain text so the whole layout is captured in one snapshot.</summary>
    private static string RenderEmbed(Embed embed)
    {
        var text = new StringBuilder()
            .AppendLine($"Title: {embed.Title}")
            .AppendLine("Description:")
            .AppendLine(embed.Description)
            .AppendLine()
            .AppendLine($"Footer: {embed.Footer?.Text}");

        return text.ToString();
    }
}
