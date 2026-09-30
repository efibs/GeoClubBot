using Entities;
using FluentAssertions;
using MediatR;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.Users;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// The import carries weeks of hand-kept points, so it is all or nothing: a single name the bot cannot
/// pin to exactly one player stops the whole import, and a corrected list simply replaces the last one.
/// </summary>
public sealed class ImportCountryChallengeStandingsHandlerTests
{
    private const string LinkedId = "5f1b2c3d4e5f6a7b8c9d0e1f";

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly ICountryChallengeRepository _repository = Substitute.For<ICountryChallengeRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly List<CountryChallengePointAward> _imported = [];

    public ImportCountryChallengeStandingsHandlerTests()
    {
        var plan = Plan(File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Leaderboard = new LeaderboardSection { Season = "Season 3" }
        });
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Success(plan));

        // Like the real repository, nicknames match ignoring case.
        var known = new[]
        {
            GeoGuessrUser.Create("fibs-id", "Fibs"),
            GeoGuessrUser.Create("anna-id", "Anna"),
            GeoGuessrUser.Create("twin-1", "Twin"),
            GeoGuessrUser.Create("twin-2", "twin")
        };
        _users.ReadUsersByNicknameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => known.Where(u => string.Equals(u.Nickname, call.Arg<string>(), StringComparison.OrdinalIgnoreCase)).ToList());

        _mediator.Send(Arg.Any<ReadOrSyncGeoGuessrUserByUserIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("user", "not found")));
        _mediator.Send(Arg.Is<ReadOrSyncGeoGuessrUserByUserIdQuery>(q => q.UserId == LinkedId), Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Success(GeoGuessrUser.Create(LinkedId, "Stranger")));

        _repository.When(r => r.ReplaceImportedAwardsAsync(Arg.Any<string>(), Arg.Any<IEnumerable<CountryChallengePointAward>>(), Arg.Any<CancellationToken>()))
            .Do(call => _imported.AddRange(call.Arg<IEnumerable<CountryChallengePointAward>>()));
        _repository.ReadAwardsAsync("Season 3", Arg.Any<CancellationToken>()).Returns(_ => [.. _imported]);
    }

    [Fact]
    public async Task Handle_ImportsNicknamesAndProfileLinks_IntoTheCurrentSeason()
    {
        var result = await HandleAsync($"1. Fibs 12\n2. https://www.geoguessr.com/user/{LinkedId} 7\n3. Anna 0");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new { Season = "Season 3", PlayerCount = 3, TotalPoints = 19 });
        _imported.Select(a => (a.Season, a.UserId, a.Nickname, a.Points, a.Source)).Should().Equal(
            ("Season 3", "fibs-id", "Fibs", 12, CountryChallengePointSource.Import),
            ("Season 3", LinkedId, "Stranger", 7, CountryChallengePointSource.Import));
        result.Value.Top.Select(s => (s.Rank, s.Nickname)).Should().Equal((1, "Fibs"), (2, "Stranger"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ImportsNothing_WhenANameCannotBePinnedToExactlyOnePlayer()
    {
        var result = await HandleAsync("Fibs 12\nNobody 3\nTwin 2\nfibs 1\nbad line\nabcdefabcdefabcdefabcdef 4");

        result.IsFailure.Should().BeTrue();
        var lines = result.Error.Message.Split('\n');
        lines[0].Should().StartWith("Nothing was imported.");
        lines.Skip(1).Should().SatisfyRespectively(
            l => l.Should().StartWith("• Line 2: 'Nobody' is not a GeoGuessr player the bot knows"),
            l => l.Should().StartWith("• Line 3: several players are called 'Twin'").And.Contain("https://www.geoguessr.com/user/twin-2"),
            l => l.Should().Be("• Line 4: Fibs is already on line 1."),
            l => l.Should().Be("• Line 5: 'bad line' is not '<player> <points>'."),
            l => l.Should().Be("• Line 6: no GeoGuessr player has the id abcdefabcdefabcdefabcdef."));
        await _repository.DidNotReceiveWithAnyArgs().ReplaceImportedAwardsAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_RejectsAnEmptyImport()
    {
        var result = await HandleAsync("# nothing here\n\n");

        result.Error.Message.Should().StartWith("Nothing to import.");
    }

    [Fact]
    public async Task Handle_NeedsAWorkingFile_ToKnowTheSeason()
    {
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Failure(Error.Validation("x", "broken")));

        var result = await HandleAsync("Fibs 12");

        result.Error.Message.Should().Be("broken");
    }

    private Task<Result<CountryChallengeStandingsImport>> HandleAsync(string text) =>
        new ImportCountryChallengeStandingsHandler(_mediator, _users, _repository, _unitOfWork)
            .Handle(new ImportCountryChallengeStandingsCommand(text), CancellationToken.None);
}
