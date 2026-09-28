using Entities;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

public sealed class PostCountryChallengeLeaderboardHandlerTests
{
    private static readonly DateTimeOffset AwardedAt = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly ICountryChallengeRepository _repository = Substitute.For<ICountryChallengeRepository>();
    private readonly IDiscordMessageAccess _discord = Substitute.For<IDiscordMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly List<CountryChallengeLeaderboardPost> _records = [];

    public PostCountryChallengeLeaderboardHandlerTests()
    {
        _repository.ReadAwardsAsync("Season 1", Arg.Any<CancellationToken>()).Returns(
        [
            CountryChallengePointAward.Imported("Season 1", "a", "Anna", 9, AwardedAt),
            CountryChallengePointAward.Imported("Season 1", "b", "Bert", 4, AwardedAt)
        ]);
        _repository.When(r => r.AddLeaderboardPost(Arg.Any<CountryChallengeLeaderboardPost>()))
            .Do(call => _records.Add(call.Arg<CountryChallengeLeaderboardPost>()));
    }

    private static CountryChallengePlan MondayPlan(List<ulong>? roleIds = null) => Plan(File(
        Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
    {
        Leaderboard = new LeaderboardSection { RoleIds = roleIds, ChannelId = 42 }
    });

    [Fact]
    public async Task Handle_PostsTheSeasonsLeaderboard_AndRecordsThatItDid()
    {
        var outcome = await HandleAsync(MondayPlan(), Monday);

        outcome.Status.Should().Be(CountryChallengeLeaderboardStatus.Posted);
        _records.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Date = Monday, Season = "Season 1" });
        await _discord.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.Contains(":first_place: Anna · 9 pts") && m.Contains(":second_place: Bert · 4 pts")),
            42UL,
            Arg.Any<MessageMentions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_OnADayItIsNotDue()
    {
        var outcome = await HandleAsync(MondayPlan(), Monday.AddDays(1));

        outcome.Status.Should().Be(CountryChallengeLeaderboardStatus.NotDue);
        await _repository.DidNotReceiveWithAnyArgs().ReadAwardsAsync(default!, default);
    }

    [Fact]
    public async Task Handle_DoesNotPostTwice_OnTheSameDay()
    {
        _repository.IsLeaderboardPostedOnAsync(Monday, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await HandleAsync(MondayPlan(), Monday);

        outcome.Status.Should().Be(CountryChallengeLeaderboardStatus.AlreadyPosted);
        await _discord.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default, default(MessageMentions)!, default);
    }

    [Fact]
    public async Task Handle_PostsNothing_WhileNobodyHasPoints()
    {
        _repository.ReadAwardsAsync("Season 1", Arg.Any<CancellationToken>()).Returns([]);

        var outcome = await HandleAsync(MondayPlan(), Monday);

        outcome.Status.Should().Be(CountryChallengeLeaderboardStatus.Empty);
        _records.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForgetsThePost_WhenDiscordRefusesIt()
    {
        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Discord is down"));

        var outcome = await HandleAsync(MondayPlan(roleIds: [1]), Monday);

        outcome.Status.Should().Be(CountryChallengeLeaderboardStatus.Failed);
        _repository.Received(1).RemoveLeaderboardPost(_records.Single());
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<DistributeCountryChallengeRolesCommand>(), default);
    }

    [Fact]
    public async Task Handle_HandsOutTheLeaderboardRolesByRank()
    {
        await HandleAsync(MondayPlan(roleIds: [1, 2]), Monday);

        await _mediator.Received(1).Send(
            Arg.Is<DistributeCountryChallengeRolesCommand>(c =>
                c.Assignment.PlayersByPlace[0].SequenceEqual(new[] { "a" })
                && c.Assignment.PlayersByPlace[1].SequenceEqual(new[] { "b" })),
            Arg.Any<CancellationToken>());
    }

    private Task<CountryChallengeLeaderboardOutcome> HandleAsync(CountryChallengePlan plan, DateOnly date) =>
        new PostCountryChallengeLeaderboardHandler(
                _repository,
                _discord,
                _mediator,
                _unitOfWork,
                NullLogger<PostCountryChallengeLeaderboardHandler>.Instance)
            .Handle(new PostCountryChallengeLeaderboardCommand(plan, date), CancellationToken.None);
}
