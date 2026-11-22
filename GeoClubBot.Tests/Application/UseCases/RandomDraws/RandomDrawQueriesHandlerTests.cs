using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.RandomDraws;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.RandomDraws;

public sealed class RandomDrawQueriesHandlerTests
{
    private static readonly Guid MainClubId = Guid.NewGuid();
    private static readonly Guid SecondClubId = Guid.NewGuid();

    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();

    public RandomDrawQueriesHandlerTests()
    {
        _clubs.ReadAllClubsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<Entities.Club> { Entities.Club.Create(MainClubId, "Main Club", 10), Entities.Club.Create(SecondClubId, "Second Club", 5) });
    }

    private RandomDrawQueriesHandler Handler() => new(
        _clubs,
        _members,
        Options.Create(new RandomDrawsConfiguration()),
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "x",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = MainClubId, IsMain = true, NcfaToken = "x" }]
        }));

    [Fact]
    public async Task WithoutAClub_DrawsFromTheMainClub()
    {
        _members.ReadClubMembersByClubIdAsync(MainClubId, Arg.Any<CancellationToken>()).Returns(
        [
            new ClubMemberBuilder().WithNickname("zed").InClub(MainClubId).Build(),
            new ClubMemberBuilder().WithNickname("Alice").WithDiscordUserId(42).InClub(MainClubId).Build()
        ]);

        var result = await Handler().Handle(new ReadDrawableClubMembersQuery(null, AllClubs: false), CancellationToken.None);

        result.Value.PoolName.Should().Be("Main Club");
        result.Value.Members.Select(m => m.Nickname).Should().Equal("Alice", "zed");
        result.Value.Members[0].DiscordUserId.Should().Be(42);
    }

    [Fact]
    public async Task AllClubs_TakesEveryCurrentMember_AndNamesTheirClub()
    {
        _members.ReadClubMembersAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ClubMemberBuilder().WithNickname("Main").InClub(MainClubId).Build(),
            new ClubMemberBuilder().WithNickname("Second").InClub(SecondClubId).Build(),
            new ClubMemberBuilder().WithNickname("Gone").InClub(null).Build()
        ]);

        var result = await Handler().Handle(new ReadDrawableClubMembersQuery(null, AllClubs: true), CancellationToken.None);

        result.Value.PoolName.Should().Be(RandomDrawQueriesHandler.AllClubsPoolName);
        result.Value.Members.Select(m => (m.Nickname, m.ClubName)).Should().Equal(("Main", "Main Club"), ("Second", "Second Club"));
    }

    [Fact]
    public async Task AnUnknownClub_IsNotFound()
    {
        var result = await Handler().Handle(new ReadDrawableClubMembersQuery(Guid.NewGuid(), AllClubs: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
