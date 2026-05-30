using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Club;
using Xunit;
using DomainClub = Entities.Club;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the GeoGuessr-orchestration club use cases (<see cref="CheckClubLevelCommand"/> and
/// <see cref="SyncClubsCommand"/>) end-to-end through the real MediatR pipeline. The GeoGuessr API
/// client is substituted to return crafted <see cref="ClubDto"/>s; the test asserts the persisted
/// club / member state plus the Discord status / level-up side effects.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubOrchestrationUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    private MediatorTestHost CreateHost(Guid mainClubId) =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true }],
            })));

    private static ClubMemberDto BuildMemberDto(string userId, string nickname, int xp) => new()
    {
        User = new ClubMemberUserDto
        {
            UserId = userId,
            Nick = nickname,
            Avatar = "",
            FullBodyAvatar = "",
            BorderUrl = "",
            IsVerified = false,
            Flair = 0,
            CountryCode = "",
            TierId = 0,
            ClubUserType = 0,
        },
        Role = 0,
        JoinedAt = DateTimeOffset.UtcNow.AddMonths(-2),
        Xp = xp,
        WeeklyXp = 0,
    };

    private static ClubDto BuildClubDto(Guid clubId, string name, int level, List<ClubMemberDto>? members = null) => new()
    {
        ClubId = clubId,
        Name = name,
        Members = members ?? [],
        JoinRule = 0,
        Tag = "TAG",
        Description = "",
        CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
        Language = "en",
        MemberCount = members?.Count ?? 0,
        MaxMemberCount = 100,
        Level = level,
        Xp = 0,
        Labels = [],
        Stats = new ClubStatsDto
        {
            ClubId = clubId,
            TotalXp = 0,
            ChangePercentXp = 0,
            TotalGamesPlayed = 0,
            ChangePercentGamesPlayed = 0,
            TotalWins = 0,
            ChangePercentWins = 0,
            TotalPerfectGuesses = 0,
            ChangePercentPerfectGuesses = 0,
            GlobalXpRank = 0,
            TotalClubs = 0,
            AverageDivision = new ClubAverageDivisionDto { Number = 0, Name = "", Tier = 0 },
        },
        BackgroundUrl = "",
    };

    private void ArrangeClubResponse(MediatorTestHost host, Guid clubId, ClubDto clubDto)
    {
        var client = Substitute.For<IGeoGuessrClient>();
        client.ReadClubAsync(clubId, Arg.Any<CancellationToken>()).Returns(clubDto);
        host.Mock<IGeoGuessrClientFactory>().CreateClient(clubId).Returns(client);
    }

    [Fact]
    public async Task CheckClubLevel_UpdatesTheClubAndSendsLevelUp_WhenTheLevelIncreased()
    {
        var clubId = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(clubId, $"club-{clubId:N}", level: 5));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        ArrangeClubResponse(host, clubId, BuildClubDto(clubId, $"club-{clubId:N}", level: 6));

        await host.SendAsync(new CheckClubLevelCommand());

        await using var read = fixture.CreateDbContext();
        var club = await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubId);
        club.Level.Should().Be(6);

        await host.Mock<IClubEventNotifier>()
            .Received()
            .SendClubLevelUpEvent(Arg.Is<DomainClub>(c => c.ClubId == clubId), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordStatusUpdater>()
            .Received()
            .UpdateStatusAsync("Level 6 club!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckClubLevel_DoesNothing_WhenTheLevelIsUnchanged()
    {
        var clubId = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(clubId, $"club-{clubId:N}", level: 5));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        ArrangeClubResponse(host, clubId, BuildClubDto(clubId, $"club-{clubId:N}", level: 5));

        await host.SendAsync(new CheckClubLevelCommand());

        await using var read = fixture.CreateDbContext();
        var club = await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubId);
        club.Level.Should().Be(5);

        await host.Mock<IClubEventNotifier>()
            .DidNotReceive()
            .SendClubLevelUpEvent(Arg.Any<DomainClub>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckClubLevel_SeedsTheTrackerWithoutLevelUp_OnTheFirstObservation()
    {
        // Club is configured but not yet persisted (e.g. before the first sync). The handler should
        // seed its tracker and update the Discord status, but must not announce a (spurious) level-up.
        var clubId = Guid.NewGuid();

        using var host = CreateHost(clubId);
        ArrangeClubResponse(host, clubId, BuildClubDto(clubId, $"club-{clubId:N}", level: 3));

        await host.SendAsync(new CheckClubLevelCommand());

        await host.Mock<IClubEventNotifier>()
            .DidNotReceive()
            .SendClubLevelUpEvent(Arg.Any<DomainClub>(), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordStatusUpdater>()
            .Received()
            .UpdateStatusAsync("Level 3 club!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncClubs_PersistsTheClubAndItsMembers_AndUpdatesStatus()
    {
        var clubId = Guid.NewGuid();
        var memberA = (userId: NewUserId(), nickname: NewNickname(), xp: 1200);
        var memberB = (userId: NewUserId(), nickname: NewNickname(), xp: 800);

        using var host = CreateHost(clubId);
        ArrangeClubResponse(host, clubId, BuildClubDto(clubId, $"club-{clubId:N}", level: 7,
        [
            BuildMemberDto(memberA.userId, memberA.nickname, memberA.xp),
            BuildMemberDto(memberB.userId, memberB.nickname, memberB.xp),
        ]));

        await host.SendAsync(new SyncClubsCommand());

        await using var read = fixture.CreateDbContext();
        var club = await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubId);
        club.Level.Should().Be(7);

        var repo = new EfClubMemberRepository(read);
        var persistedA = await repo.ReadClubMemberByUserIdAsync(memberA.userId);
        var persistedB = await repo.ReadClubMemberByUserIdAsync(memberB.userId);
        persistedA.Should().NotBeNull();
        persistedA!.Xp.Should().Be(1200);
        persistedA.ClubId.Should().Be(clubId);
        persistedB.Should().NotBeNull();
        persistedB!.Xp.Should().Be(800);

        await host.Mock<IDiscordStatusUpdater>()
            .Received()
            .UpdateStatusAsync("Level 7 club!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncClubs_ClearsTheClubMembership_ForAMemberNoLongerInTheApiRoster()
    {
        var clubId = Guid.NewGuid();
        var leaver = (userId: NewUserId(), nickname: NewNickname());
        var stayer = (userId: NewUserId(), nickname: NewNickname(), xp: 500);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainClub.Create(clubId, $"club-{clubId:N}", level: 4));
            var user = GeoGuessrUser.Create(leaver.userId, leaver.nickname);
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 999, joinedAt: DateTimeOffset.UtcNow.AddMonths(-6)));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        ArrangeClubResponse(host, clubId, BuildClubDto(clubId, $"club-{clubId:N}", level: 4,
        [
            BuildMemberDto(stayer.userId, stayer.nickname, stayer.xp),
        ]));

        await host.SendAsync(new SyncClubsCommand());

        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);
        var persistedLeaver = await repo.ReadClubMemberByUserIdAsync(leaver.userId);
        persistedLeaver.Should().NotBeNull();
        persistedLeaver!.ClubId.Should().BeNull("a member absent from the API roster has left the club");

        var persistedStayer = await repo.ReadClubMemberByUserIdAsync(stayer.userId);
        persistedStayer!.ClubId.Should().Be(clubId);
    }
}
