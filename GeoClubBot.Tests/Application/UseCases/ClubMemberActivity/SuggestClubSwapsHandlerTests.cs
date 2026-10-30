using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMemberActivity.ClubSwaps;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>
/// The decisions themselves belong to <see cref="ClubSwapPlanner"/> (see its tests); this covers what
/// the handler feeds it and where the report goes.
/// </summary>
public sealed class SuggestClubSwapsHandlerTests
{
    private const ulong ActivityChannel = 1234UL;
    private const ulong SwapChannel = 777UL;
    private const int HistoryDepth = 3;

    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Joined = DateTimeOffset.UnixEpoch;

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordMessageAccess _messages = Substitute.For<IDiscordMessageAccess>();

    private readonly SwapSuggestionsConfiguration _options = new()
    {
        Enabled = true,
        HistoryDepth = HistoryDepth,
        BufferXp = 10,
        MaxClubSize = 2
    };

    private string? _sentMessage;
    private ulong? _sentChannel;

    public SuggestClubSwapsHandlerTests()
    {
        _clubs.ReadClubByIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(MainClub, "Main Club", 1));
        _clubs.ReadClubByIdAsync(SecondClub, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(SecondClub, "Second Club", 1));

        _messages
            .When(m => m.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                _sentMessage = call.Arg<string>();
                _sentChannel = call.Arg<ulong>();
            });
    }

    private SuggestClubSwapsHandler CreateHandler(bool withSecondClub = true)
    {
        var geoGuessr = new GeoGuessrConfigurationBuilder().WithClub(MainClub);
        if (withSecondClub)
        {
            geoGuessr.WithClub(SecondClub, isMain: false);
        }

        return new SuggestClubSwapsHandler(
            _mediator,
            _clubs,
            _clubMembers,
            _messages,
            Options.Create(_options),
            new ActivityCheckerConfigurationBuilder().BuildOptions(),
            geoGuessr.BuildOptions(),
            NullLogger<SuggestClubSwapsHandler>.Instance);
    }

    private void Averages(Guid clubId, params ClubMemberAverageXp[] averages) =>
        _mediator.Send(new CalculateAverageXpQuery(clubId, HistoryDepth), Arg.Any<CancellationToken>())
            .Returns(averages.ToList());

    private void MainMembers(params string[] userIds) =>
        _clubMembers.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(userIds.Select(id => new ClubMemberBuilder().WithUserId(id).InClub(MainClub).Build()).ToList());

    private static ClubMemberActivityStatus Status(string nickname, string userId, bool outOfStrikes, int? ruleXp = null) =>
        new(nickname, userId, false, 5, 3, outOfStrikes, 100, null, ruleXp);

    private static ClubMemberAverageXp Average(string nickname, double averageXp, string? userId = null) =>
        new(nickname, averageXp, Joined, userId ?? nickname.ToLowerInvariant());

    [Fact]
    public async Task Disabled_PostsNothingAndReadsNothing()
    {
        _options.Enabled = false;

        await CreateHandler().Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _mediator.ReceivedCalls().Should().BeEmpty();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NoSecondClub_PostsNothing()
    {
        await CreateHandler(withSecondClub: false).Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _mediator.ReceivedCalls().Should().BeEmpty();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ReportsABetterSecondClubMember_UnderBothClubNames_ToTheConfiguredChannel()
    {
        _options.TextChannelId = SwapChannel;
        Averages(MainClub, Average("Weak", 50));
        Averages(SecondClub, Average("Strong", 100));
        MainMembers("weak", "other");

        await CreateHandler().Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _sentChannel.Should().Be(SwapChannel);
        _sentMessage.Should().Contain("Swap suggestions - Main Club")
            .And.Contain("over the last 3 week(s)")
            .And.Contain("at least 10 XP more")
            .And.Contain("**Strong** (Second Club, 100.0 XP) swaps with **Weak** (50.0 XP)");
    }

    [Fact]
    public async Task WithoutAChannelOfItsOwn_PostsToTheActivityChannel()
    {
        Averages(MainClub);
        Averages(SecondClub);
        MainMembers("a", "b");

        await CreateHandler().Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _sentChannel.Should().Be(ActivityChannel);
        _sentMessage.Should().Contain("No swaps this week");
    }

    [Fact]
    public async Task UnknownClubs_FallBackToTheirIds()
    {
        _clubs.ReadClubByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);
        Averages(MainClub, Average("Weak", 50));
        Averages(SecondClub, Average("Strong", 100));
        MainMembers("weak", "other");

        await CreateHandler().Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _sentMessage.Should().Contain($"Swap suggestions - {MainClub}")
            .And.Contain($"**Strong** ({SecondClub}, 100.0 XP)");
    }

    [Fact]
    public async Task CandidatesWithoutAUserId_AreLeftOut()
    {
        Averages(MainClub, Average("Weak", 50));
        Averages(SecondClub, new ClubMemberAverageXp("Anonymous", 100, Joined));
        MainMembers("weak", "other");

        await CreateHandler().Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _sentMessage.Should().Contain("No swaps this week").And.NotContain("Anonymous");
    }

    [Fact]
    public async Task KickedMainMember_IsReplaced_UsingTheirAverage()
    {
        Averages(MainClub, Average("Kicked", 70));
        Averages(SecondClub, Average("Strong", 60));
        MainMembers("kicked", "other");

        await CreateHandler().Handle(
            new SuggestClubSwapsCommand([Status("Kicked", "kicked", outOfStrikes: true)]),
            CancellationToken.None);

        _sentMessage.Should().Contain("**Strong** (Second Club, 60.0 XP) takes the spot of **Kicked**, who is out of strikes");
    }

    [Fact]
    public async Task KickedMemberTooNewForAnAverage_IsStillReplaced()
    {
        Averages(MainClub);
        Averages(SecondClub, Average("Strong", 60));
        MainMembers("newbie", "other");

        await CreateHandler().Handle(
            new SuggestClubSwapsCommand([Status("Newbie", "newbie", outOfStrikes: true, ruleXp: 12)]),
            CancellationToken.None);

        _sentMessage.Should().Contain("takes the spot of **Newbie**");
    }

    [Fact]
    public async Task OutOfStrikesInTheSecondClub_IsNoSpotToFill()
    {
        Averages(MainClub);
        Averages(SecondClub, Average("Strong", 60));
        MainMembers("a", "b");

        await CreateHandler().Handle(
            new SuggestClubSwapsCommand([Status("Elsewhere", "second-club-member", outOfStrikes: true)]),
            CancellationToken.None);

        _sentMessage.Should().Contain("No swaps this week");
    }

    [Fact]
    public async Task MainMemberStillInStrikes_IsNotTreatedAsKicked()
    {
        Averages(MainClub, Average("Fine", 70));
        Averages(SecondClub, Average("Strong", 60));
        MainMembers("fine", "other");

        await CreateHandler().Handle(
            new SuggestClubSwapsCommand([Status("Fine", "fine", outOfStrikes: false)]),
            CancellationToken.None);

        _sentMessage.Should().Contain("No swaps this week");
    }

    [Fact]
    public async Task FreeSpots_AreCountedFromTheActualMemberList()
    {
        _options.MaxClubSize = 3;
        Averages(MainClub);
        Averages(SecondClub, Average("Strong", 60));
        MainMembers("a", "b");

        await CreateHandler().Handle(new SuggestClubSwapsCommand([]), CancellationToken.None);

        _sentMessage.Should().Contain("**Strong** (Second Club, 60.0 XP) is promoted into a free spot");
    }

    [Fact]
    public async Task PlannerOptions_ComeFromTheConfiguration()
    {
        _options.MaxClubSize = 3;
        _options.SuggestPromotions = false;
        _options.SuggestReplacementsForKicks = false;
        Averages(MainClub);
        Averages(SecondClub, Average("Strong", 60));
        MainMembers("kicked", "b");

        await CreateHandler().Handle(
            new SuggestClubSwapsCommand([Status("Kicked", "kicked", outOfStrikes: true)]),
            CancellationToken.None);

        _sentMessage.Should().Contain("No swaps this week");
    }
}
