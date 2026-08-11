using System.Text.Json;
using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyChallenge;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyChallengeTests;

/// <summary>
/// The daily challenge runs in three phases — reward the previous challenges, create the next ones,
/// publish both — and the phases must not be able to take each other down. These tests fail each
/// phase in turn and pin what still happens: what is announced, what is persisted, and which
/// challenges stay active so the next run can recover.
/// </summary>
public sealed class DailyChallengeHandlerTests : IDisposable
{
    private const ulong TextChannelId = 5;
    private const string Hard = "Hard";
    private const string Easy = "Easy";

    private static readonly Guid MainClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IGeoGuessrClientFactory _factory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IClubChallengeRepository _challenges = Substitute.For<IClubChallengeRepository>();
    private readonly IDiscordMessageAccess _discord = Substitute.For<IDiscordMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly string _configFilePath = Path.Combine(Path.GetTempPath(), $"challenges-{Guid.NewGuid():N}.json");

    /// <summary>The messages the handler posted, in order.</summary>
    private readonly List<string> _postedMessages = [];

    public DailyChallengeHandlerTests()
    {
        _factory.CreateClient(MainClubId).Returns(_client);
        _client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new PostChallengeResponseDto { Token = "new-token" });
        _client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores());
        _challenges.ReadLatestClubChallengeLinksAsync(Arg.Any<CancellationToken>()).Returns([]);

        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _postedMessages.Add(callInfo.ArgAt<string>(0));
                return Task.CompletedTask;
            });

        WriteConfiguration((Hard, 2), (Easy, 1));
    }

    public void Dispose() => File.Delete(_configFilePath);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task Handle_RewardsCreatesAndPublishes()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"), (Easy, 1, "old-easy"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("player-1"));

        await HandleAsync();

        await _mediator.Received().Send(
            Arg.Is<DistributeDailyChallengeRolesCommand>(c => c!.Results.Count == 2),
            Arg.Any<CancellationToken>());
        _challenges.Received().AddLatestClubChallengeLinks(
            Arg.Is<IEnumerable<ClubChallengeLink>>(links => links!.Count() == 2));
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
        _postedMessages.Should().Contain(m => m.Contains("The results are in!"))
            .And.Contain(m => m.Contains("Next challenges"));
    }

    // ---- Phase 1 (reward) fails -------------------------------------------

    [Fact]
    public async Task Handle_StillCreatesAndPublishesTheNextChallenges_WhenTheHighscoresCannotBeRead()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        await HandleAsync();

        _challenges.Received().AddLatestClubChallengeLinks(
            Arg.Is<IEnumerable<ClubChallengeLink>>(links => links!.Count() == 2));
        _postedMessages.Should().Contain(m => m.Contains("results of the last challenges could not be determined"))
            .And.Contain(m => m.Contains("Next challenges"));
    }

    [Fact]
    public async Task Handle_StillCreatesAndPublishesTheNextChallenges_WhenTheRolesCannotBeDistributed()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("player-1"));
        _mediator.Send(Arg.Any<DistributeDailyChallengeRolesCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Discord is down"));

        await HandleAsync();

        // The results were read, so they are published even though the roles could not be handed out.
        _postedMessages.Should().Contain(m => m.Contains("The results are in!"))
            .And.Contain(m => m.Contains("Next challenges"));
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- Phase 2 (create) fails -------------------------------------------

    [Fact]
    public async Task Handle_StillPublishesTheResults_WhenNoChallengeCanBeCreated()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("player-1"));
        _client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        await HandleAsync();

        await _mediator.Received().Send(Arg.Any<DistributeDailyChallengeRolesCommand>(), Arg.Any<CancellationToken>());
        _postedMessages.Should().Contain(m => m.Contains("The results are in!"))
            .And.Contain(m => m.Contains("No new challenges could be created"));
    }

    [Fact]
    public async Task Handle_KeepsTheActiveChallenges_WhenNoChallengeCanBeCreated()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"), (Easy, 1, "old-easy"));
        _client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        await HandleAsync();

        _challenges.DidNotReceive().AddLatestClubChallengeLinks(Arg.Any<IEnumerable<ClubChallengeLink>>());
        _challenges.DidNotReceive().DeleteLatestClubChallengeLinks(Arg.Any<IEnumerable<ClubChallengeLink>>());
    }

    [Fact]
    public async Task Handle_KeepsTheActiveChallengeOfTheDifficultyThatFailed_AndReplacesTheOthers()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"), (Easy, 1, "old-easy"));
        _client.CreateChallengeAsync(
                Arg.Is<PostChallengeRequestDto>(r => r!.Map == MapIdOf(Hard)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        await HandleAsync();

        _challenges.Received().AddLatestClubChallengeLinks(
            Arg.Is<IEnumerable<ClubChallengeLink>>(links => links!.Single().Difficulty == Easy));
        _challenges.Received().DeleteLatestClubChallengeLinks(
            Arg.Is<IEnumerable<ClubChallengeLink>>(links => links!.Single().ChallengeId == "old-easy"));
        _postedMessages.Should().Contain(m => m.Contains($"{Hard}: ERROR"));
    }

    [Fact]
    public async Task Handle_DoesNotAnnounceChallengesItCouldNotStore()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("player-1"));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the database is down"));

        await HandleAsync();

        _postedMessages.Should().Contain(m => m.Contains("The results are in!"))
            .And.Contain(m => m.Contains("No new challenges could be created"))
            .And.NotContain(m => m.Contains("Next challenges"));
    }

    [Fact]
    public async Task Handle_ReportsTheFailure_WhenTheConfigurationCannotBeRead()
    {
        File.Delete(_configFilePath);

        await HandleAsync();

        _postedMessages.Should().ContainSingle()
            .Which.Should().Contain("No new challenges could be created");
    }

    // ---- Both phases fail -------------------------------------------------

    [Fact]
    public async Task Handle_PostsASingleFailureNotice_WhenNeitherPhaseDelivered()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));
        _client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        await HandleAsync();

        _postedMessages.Should().ContainSingle()
            .Which.Should().Contain("The daily challenge could not be run");
        _challenges.DidNotReceive().DeleteLatestClubChallengeLinks(Arg.Any<IEnumerable<ClubChallengeLink>>());
    }

    // ---- Phase 3 (publish) fails ------------------------------------------

    [Fact]
    public async Task Handle_StillAnnouncesTheNextChallenges_WhenPublishingTheResultsFails()
    {
        ArrangeActiveChallenges((Hard, 2, "old-hard"));
        _client.ReadHighscoresAsync("old-hard", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("player-1"));
        _discord.SendMessageAsync(Arg.Is<string>(m => m!.Contains("The results are in!")), TextChannelId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Discord is down"));

        await HandleAsync();

        _postedMessages.Should().Contain(m => m.Contains("Next challenges"));
    }

    [Fact]
    public async Task Handle_DoesNotThrow_WhenDiscordIsUnreachable()
    {
        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Discord is down"));

        var act = async () => await HandleAsync();

        await act.Should().NotThrowAsync("the challenges are already stored; the job has nothing left to retry");
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- Helpers ----------------------------------------------------------

    private Task HandleAsync() =>
        new DailyChallengeHandler(
                _factory,
                _challenges,
                _discord,
                _mediator,
                _unitOfWork,
                NullLogger<DailyChallengeHandler>.Instance,
                Options.Create(new DailyChallengesConfiguration
                {
                    Schedule = "0 0 0 * * ?",
                    TextChannelId = TextChannelId,
                    ConfigurationFilePath = _configFilePath,
                    FirstRoleId = 100,
                    SecondRoleId = 200,
                    ThirdRoleId = 300,
                }),
                new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions())
            .Handle(new DailyChallengeCommand(), CancellationToken.None);

    private void ArrangeActiveChallenges(params (string Difficulty, int RolePriority, string ChallengeId)[] links) =>
        _challenges.ReadLatestClubChallengeLinksAsync(Arg.Any<CancellationToken>())
            .Returns(links.Select(l => ClubChallengeLink.Create(l.Difficulty, l.RolePriority, l.ChallengeId)).ToList());

    private static string MapIdOf(string difficulty) => $"map-{difficulty}";

    private void WriteConfiguration(params (string Difficulty, int RolePriority)[] difficulties)
    {
        var configuration = difficulties
            .Select(d => new ClubChallengeConfigurationDifficulty(d.Difficulty)
            {
                RolePriority = d.RolePriority,
                Entries =
                [
                    new ClubChallengeConfigurationDifficultyEntry(
                        Description: $"{d.Difficulty} map", MapId: MapIdOf(d.Difficulty), ForbidMoving: true,
                        ForbidRotating: false, ForbidZooming: false, TimeLimit: 60),
                ],
            })
            .ToList();

        File.WriteAllText(_configFilePath, JsonSerializer.Serialize(configuration));
    }

    private static ChallengeResultHighscoresDto Highscores(params string[] userIds) => new()
    {
        Items = userIds.Select(id => new ChallengeResultItemDto
        {
            Game = new ChallengeResultGameDto
            {
                Player = new ChallengeResultPlayerDto
                {
                    Id = id,
                    Nick = $"nick-{id}",
                    TotalScore = new ChallengeResultPlayerScoreDto { Amount = "5000", Unit = "points" },
                    TotalDistance = new ChallengeResultPlayerDistanceDto
                    {
                        Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = "1000", Unit = "km" },
                    },
                },
            },
        }).ToList(),
    };
}
