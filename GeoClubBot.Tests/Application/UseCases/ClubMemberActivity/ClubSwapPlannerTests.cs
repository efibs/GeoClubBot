using Entities;
using FluentAssertions;
using UseCases.UseCases.ClubMemberActivity.ClubSwaps;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ClubSwapPlannerTests
{
    private static readonly SwapPlanOptions Options = new(BufferXp: 10, MaxClubSize: 3, ReplaceKickedMembers: true, FillFreeSpots: true);

    private static ClubMemberAverageXp MainMember(string name, double average, int joinedDaysAgo = 100) =>
        new(name, average, DateTimeOffset.UnixEpoch.AddDays(1000 - joinedDaysAgo), $"id-{name}");

    private static SwapCandidate Den(string name, double average) => new($"id-{name}", name, average, "Dragon's Den");

    [Fact]
    public void Plan_SwapsOnlyWhenTheBufferIsBeaten()
    {
        var suggestions = ClubSwapPlanner.Plan(
            [MainMember("A", 150), MainMember("B", 120), MainMember("C", 100)],
            [Den("X", 115), Den("Y", 109)],
            kickedMainMembers: [],
            mainClubMemberCount: 3,
            Options);

        // X beats C (100) by 15; Y would need 110 against C and 130 against B.
        suggestions.Should().ContainSingle();
        suggestions[0].Should().Be(new SwapSuggestion(Den("X", 115), MainMember("C", 100), SwapReason.BetterAverage));
    }

    [Fact]
    public void Plan_PairsTheBestCandidatesWithTheWeakestMembers()
    {
        var suggestions = ClubSwapPlanner.Plan(
            [MainMember("A", 150), MainMember("B", 60), MainMember("C", 50)],
            [Den("X", 70), Den("Y", 140)],
            kickedMainMembers: [],
            mainClubMemberCount: 3,
            Options);

        suggestions.Select(s => (s.Incoming.Nickname, s.Outgoing!.Nickname))
            .Should().Equal(("Y", "C"), ("X", "B"));
    }

    [Fact]
    public void Plan_ReplacesKickedMembersFirst_ThenFillsFreeSpots_ThenSwaps()
    {
        var kicked = MainMember("K", 20);

        var suggestions = ClubSwapPlanner.Plan(
            [kicked, MainMember("B", 50)],
            [Den("X", 40), Den("Y", 90), Den("Z", 70)],
            kickedMainMembers: [kicked],
            mainClubMemberCount: 2,
            Options);

        suggestions.Select(s => s.Reason).Should().StartWith([SwapReason.ReplacesKickedMember, SwapReason.FillsFreeSpot]);
        suggestions[0].Incoming.Nickname.Should().Be("Y");
        suggestions[1].Incoming.Nickname.Should().Be("Z");
        suggestions.Should().HaveCount(2, "X (40) does not beat B (50) by 10, and the kicked member is not swapped again");
    }

    [Fact]
    public void Plan_CanTurnOffReplacementsAndPromotions()
    {
        var kicked = MainMember("K", 20);

        var suggestions = ClubSwapPlanner.Plan(
            [kicked, MainMember("B", 50)],
            [Den("Y", 90)],
            kickedMainMembers: [kicked],
            mainClubMemberCount: 2,
            Options with { ReplaceKickedMembers = false, FillFreeSpots = false });

        suggestions.Should().ContainSingle().Which.Outgoing!.Nickname.Should().Be("B");
    }

    [Fact]
    public void Plan_SuggestsNothing_WithoutCandidates()
    {
        ClubSwapPlanner.Plan([MainMember("A", 10)], [], [], 1, Options).Should().BeEmpty();
    }

    [Fact]
    public Task Message_RendersEveryKindOfSuggestion()
    {
        var suggestions = new List<SwapSuggestion>
        {
            new(Den("Yara", 188.5), MainMember("Kai", 60), SwapReason.ReplacesKickedMember),
            new(Den("Zed", 170), null, SwapReason.FillsFreeSpot),
            new(Den("Xeno", 155), MainMember("Bo", 130.25), SwapReason.BetterAverage)
        };

        return Verify(ClubSwapSuggestionMessage.Render("Dragon", suggestions, historyDepth: 2, bufferXp: 10));
    }

    [Fact]
    public Task Message_SaysSo_WhenThereAreNoSwaps() =>
        Verify(ClubSwapSuggestionMessage.Render("Dragon", [], historyDepth: 2, bufferXp: 10));
}
