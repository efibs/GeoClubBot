using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.MissionBoard;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// The mission board use cases end-to-end through the real MediatR pipeline against Postgres: the
/// stuck-mission alert (whose idempotency lives in the database) and the board query.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MissionBoardUseCaseIntegrationTests(PostgresFixture fixture)
{
    private const ulong AlertChannelId = 4242;

    private MediatorTestHost CreateHost(Guid clubId, bool dmClaimer = false) =>
        new(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = clubId, NcfaToken = "x", IsMain = true }],
            }));
            services.AddSingleton(Options.Create(new MissionBoardAlertsConfiguration
            {
                Enabled = true,
                Schedule = "0 0 0 * * ?",
                TextChannelId = AlertChannelId,
                OpenClaimAlertAfter = TimeSpan.FromHours(6),
                HelpRequestAlertAfter = TimeSpan.FromHours(1),
                MentionClaimer = true,
                DmClaimer = dmClaimer
            }));
        });

    private async Task<(string UserId, ulong DiscordUserId)> SeedClaimerAsync(Guid clubId)
    {
        var userId = Guid.NewGuid().ToString("N")[..24];
        var discordUserId = (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);
        await using var db = fixture.CreateDbContext();
        db.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
        var user = GeoGuessrUser.Create(userId, $"nick-{userId[..10]}", discordUserId);
        db.Add(user);
        db.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
        await db.SaveChangesAsync();
        return (userId, discordUserId);
    }

    [Fact]
    public async Task StuckMissionAlert_IsSentOncePerMissionAndKind_HoweverOftenTheCheckRuns()
    {
        var clubId = Guid.NewGuid();
        var (userId, discordUserId) = await SeedClaimerAsync(clubId);
        var stuck = Tile(claimedBy: userId, claimedAt: DateTimeOffset.UtcNow.AddHours(-8), helpRequestedAt: DateTimeOffset.UtcNow.AddHours(-2));

        using var host = CreateHost(clubId, dmClaimer: true);
        host.Mock<IClubMissionBoardReader>().ReadCurrentAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, [stuck, .. FreeTiles(3)])));
        host.Mock<IDiscordDirectMessageAccess>()
            .SendDirectMessageAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await host.SendAsync(new CheckStuckMissionsCommand());
        await host.SendAsync(new CheckStuckMissionsCommand());

        // One open-claim and one help-request alert, both pinging only the claimer.
        await host.Mock<IDiscordMessageAccess>().Received(2).SendMessageAsync(
            Arg.Any<string>(), AlertChannelId,
            Arg.Is<MessageMentions>(m => m!.UserIds.Single() == discordUserId && m.RoleIds.Count == 0 && !m.Everyone),
            Arg.Any<CancellationToken>());
        await host.Mock<IDiscordMessageAccess>().Received(1).SendMessageAsync(
            Arg.Is<string>(s => s!.Contains($"<@{discordUserId}>") && s.Contains("needs help")), AlertChannelId,
            Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>());
        // The DM goes out for the open claim only.
        await host.Mock<IDiscordDirectMessageAccess>().Received(1)
            .SendDirectMessageAsync(discordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var recorded = await read.MissionBoardAlerts.AsNoTracking().Where(a => a.ClubId == clubId).ToListAsync();
        recorded.Select(a => a.Kind).Should().BeEquivalentTo([MissionBoardAlertKind.OpenClaim, MissionBoardAlertKind.HelpRequest]);
        recorded.Should().OnlyContain(a => a.MissionId == stuck.MissionId);
    }

    [Fact]
    public async Task StuckMissionAlert_DoesNothing_WhileDisabled()
    {
        var clubId = Guid.NewGuid();
        using var host = new MediatorTestHost(fixture.ConnectionString);

        await host.SendAsync(new CheckStuckMissionsCommand());

        await host.Mock<IClubMissionBoardReader>().DidNotReceive().ReadCurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        (await fixture.CreateDbContext().MissionBoardAlerts.CountAsync(a => a.ClubId == clubId)).Should().Be(0);
    }

    [Fact]
    public async Task BoardQuery_NamesTheClaimers_FromTheRoster()
    {
        var clubId = Guid.NewGuid();
        var (userId, _) = await SeedClaimerAsync(clubId);

        using var host = CreateHost(clubId);
        host.Mock<IClubMissionBoardReader>().ReadCurrentAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: userId), Tile(index: 1, claimedBy: "stranger"))));

        var result = await host.SendAsync(new GetClubMissionBoardQuery(null));

        result.IsSuccess.Should().BeTrue();
        result.Value.ClubName.Should().Be($"club-{clubId:N}");
        result.Value.NicknameOf(userId).Should().Be($"nick-{userId[..10]}");
        result.Value.NicknameOf("stranger").Should().Be("stranger", "unknown claimers fall back to their id");
    }

    [Fact]
    public async Task BoardQuery_ReturnsNotFound_ForAClubTheBotDoesNotTrack()
    {
        using var host = CreateHost(Guid.NewGuid());

        var result = await host.SendAsync(new GetClubMissionBoardQuery(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
