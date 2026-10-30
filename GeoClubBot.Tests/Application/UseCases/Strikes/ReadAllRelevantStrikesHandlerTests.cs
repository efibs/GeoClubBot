using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Strikes;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Strikes;

public sealed class ReadAllRelevantStrikesHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _mainClient = Substitute.For<IGeoGuessrClient>();
    private readonly IGeoGuessrClient _secondClient = Substitute.For<IGeoGuessrClient>();
    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();

    public ReadAllRelevantStrikesHandlerTests()
    {
        _clientFactory.CreateClient(MainClub).Returns(_mainClient);
        _clientFactory.CreateClient(SecondClub).Returns(_secondClient);
    }

    private ReadAllRelevantStrikesHandler CreateHandler(GeoGuessrConfigurationBuilder config) =>
        new(_clientFactory, TestScopes.Serving(services => services.AddSingleton(_strikes)), config.BuildOptions());

    private void StrikeCountsAre(params (string UserId, int Count)[] counts) =>
        _strikes.ReadActiveStrikeCountsByMemberUserIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(counts.ToDictionary(c => c.UserId, c => c.Count));

    [Fact]
    public async Task ListsOnlyMembersWithActiveStrikes()
    {
        _mainClient.ReadClubMembersAsync(MainClub, Arg.Any<CancellationToken>()).Returns(
        [
            GeoGuessrDtos.Member("struck", "Struck"),
            GeoGuessrDtos.Member("zero", "Zero"),
            GeoGuessrDtos.Member("absent", "Absent")
        ]);
        StrikeCountsAre(("struck", 2), ("zero", 0));

        var result = await CreateHandler(new GeoGuessrConfigurationBuilder().WithClub(MainClub))
            .Handle(new ReadAllRelevantStrikesQuery(), CancellationToken.None);

        result.Should().Equal(new ClubMemberRelevantStrike("Struck", 2));
    }

    [Fact]
    public async Task AsksTheRepositoryAboutExactlyTheCurrentMembers()
    {
        _mainClient.ReadClubMembersAsync(MainClub, Arg.Any<CancellationToken>()).Returns(
        [
            GeoGuessrDtos.Member("a", "A"),
            GeoGuessrDtos.Member("b", "B")
        ]);
        StrikeCountsAre();

        await CreateHandler(new GeoGuessrConfigurationBuilder().WithClub(MainClub))
            .Handle(new ReadAllRelevantStrikesQuery(), CancellationToken.None);

        await _strikes.Received(1).ReadActiveStrikeCountsByMemberUserIdsAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CombinesEveryClub_EachReadThroughItsOwnClient()
    {
        _mainClient.ReadClubMembersAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([GeoGuessrDtos.Member("m", "MainPlayer")]);
        _secondClient.ReadClubMembersAsync(SecondClub, Arg.Any<CancellationToken>())
            .Returns([GeoGuessrDtos.Member("s", "SecondPlayer")]);
        StrikeCountsAre(("m", 1), ("s", 3));

        var result = await CreateHandler(new GeoGuessrConfigurationBuilder().WithClub(MainClub).WithClub(SecondClub, isMain: false))
            .Handle(new ReadAllRelevantStrikesQuery(), CancellationToken.None);

        result.Should().BeEquivalentTo([
            new ClubMemberRelevantStrike("MainPlayer", 1),
            new ClubMemberRelevantStrike("SecondPlayer", 3)
        ]);
    }

    [Fact]
    public async Task NoMembersWithStrikes_GivesAnEmptyList()
    {
        _mainClient.ReadClubMembersAsync(MainClub, Arg.Any<CancellationToken>()).Returns([]);
        StrikeCountsAre();

        var result = await CreateHandler(new GeoGuessrConfigurationBuilder().WithClub(MainClub))
            .Handle(new ReadAllRelevantStrikesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
