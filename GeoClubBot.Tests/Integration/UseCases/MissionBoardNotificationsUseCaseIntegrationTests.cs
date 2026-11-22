using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.MissionBoardNotifications;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// The board change DMs (#504) end-to-end against Postgres: who may subscribe, who is told, and that
/// the stored observation makes each change go out once.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MissionBoardNotificationsUseCaseIntegrationTests(PostgresFixture fixture)
{
    private MediatorTestHost CreateHost(Guid clubId, bool enabled = true) =>
        new(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = clubId, NcfaToken = "x", IsMain = true }],
            }));
            services.AddSingleton(Options.Create(new MissionBoardNotificationsConfiguration
            {
                Enabled = enabled,
                Schedule = "0 0 0 * * ?",
            }));
        });

    private static ulong NewDiscordUserId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private async Task SeedClubAsync(Guid clubId)
    {
        await using var db = fixture.CreateDbContext();
        db.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
        await db.SaveChangesAsync();
    }

    /// <summary>A linked GeoGuessr account in <paramref name="clubId"/> (none: not in a club).</summary>
    private async Task<(string UserId, ulong DiscordUserId)> SeedMemberAsync(Guid? clubId)
    {
        var userId = Guid.NewGuid().ToString("N")[..24];
        var discordUserId = NewDiscordUserId();
        await using var db = fixture.CreateDbContext();
        var user = GeoGuessrUser.Create(userId, $"nick-{userId[..10]}", discordUserId);
        db.Add(user);
        db.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
        await db.SaveChangesAsync();
        return (userId, discordUserId);
    }

    private static void ReturnBoards(MediatorTestHost host, Guid clubId, params ClubMissionBoardWeek[] weeks) =>
        host.Mock<IClubMissionBoardReader>().ReadCurrentAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(weeks[0], weeks[1..]);

    private static IMissionBoardChangeMessages Messages(MediatorTestHost host)
    {
        var messages = host.Mock<IMissionBoardChangeMessages>();
        messages.SendAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return messages;
    }

    [Fact]
    public async Task Subscribe_NeedsALinkedAccountInOneOfTheClubs()
    {
        var clubId = Guid.NewGuid();
        await SeedClubAsync(clubId);
        var (_, outsider) = await SeedMemberAsync(clubId: null);
        using var host = CreateHost(clubId);

        var unlinked = await host.SendAsync(new SubscribeToMissionBoardCommand(NewDiscordUserId(), MissionBoardSubscriptionMode.Always));
        var notInClub = await host.SendAsync(new SubscribeToMissionBoardCommand(outsider, MissionBoardSubscriptionMode.Always));

        unlinked.Error.Code.Should().Be(MissionBoardSubscriptionHandlers.NotLinkedErrorCode);
        notInClub.Error.Code.Should().Be(MissionBoardSubscriptionHandlers.NotInClubErrorCode);
        (await host.SendAsync(new GetMissionBoardSubscriptionQuery(outsider))).Should().BeNull();
    }

    [Fact]
    public async Task Subscribe_Twice_ChangesTheMode_AndUnsubscribeEndsIt()
    {
        var clubId = Guid.NewGuid();
        await SeedClubAsync(clubId);
        var (_, member) = await SeedMemberAsync(clubId);
        using var host = CreateHost(clubId);

        (await host.SendAsync(new SubscribeToMissionBoardCommand(member, MissionBoardSubscriptionMode.Always))).Value
            .Should().Be($"club-{clubId:N}");
        await host.SendAsync(new SubscribeToMissionBoardCommand(member, MissionBoardSubscriptionMode.NextChangeOnly));
        (await host.SendAsync(new GetMissionBoardSubscriptionQuery(member))).Should().Be(MissionBoardSubscriptionMode.NextChangeOnly);

        (await host.SendAsync(new UnsubscribeFromMissionBoardCommand(member))).Should().BeTrue();
        (await host.SendAsync(new UnsubscribeFromMissionBoardCommand(member))).Should().BeFalse();
        (await host.SendAsync(new GetMissionBoardSubscriptionQuery(member))).Should().BeNull();
    }

    [Fact]
    public async Task Subscribe_WhileSwitchedOff_IsRefused()
    {
        var clubId = Guid.NewGuid();
        await SeedClubAsync(clubId);
        var (_, member) = await SeedMemberAsync(clubId);
        using var host = CreateHost(clubId, enabled: false);

        var result = await host.SendAsync(new SubscribeToMissionBoardCommand(member, MissionBoardSubscriptionMode.Always));

        result.Error.Code.Should().Be(MissionBoardSubscriptionHandlers.DisabledErrorCode);
    }

    [Fact]
    public async Task BoardUnlock_DmsTheClubsSubscribers_Once_SkippingWhoeverClaimedThisCycle()
    {
        var clubId = Guid.NewGuid();
        var otherClubId = Guid.NewGuid();
        await SeedClubAsync(clubId);
        await SeedClubAsync(otherClubId);
        var (_, always) = await SeedMemberAsync(clubId);
        var (_, once) = await SeedMemberAsync(clubId);
        var (claimerUserId, claimer) = await SeedMemberAsync(clubId);
        var (_, elsewhere) = await SeedMemberAsync(otherClubId);
        await SeedMemberAsync(clubId); // in the club, but not subscribed

        using var host = CreateHost(clubId);
        var messages = Messages(host);
        await host.SendAsync(new SubscribeToMissionBoardCommand(always, MissionBoardSubscriptionMode.Always));
        await host.SendAsync(new SubscribeToMissionBoardCommand(once, MissionBoardSubscriptionMode.NextChangeOnly));
        await host.SendAsync(new SubscribeToMissionBoardCommand(claimer, MissionBoardSubscriptionMode.Always));
        await using (var db = fixture.CreateDbContext())
        {
            // Subscribed while in this club, then moved on: the club is read at each change.
            db.Add(MissionBoardSubscription.Create(elsewhere, MissionBoardSubscriptionMode.Always, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        // The claimer took the mission that cleared board 2 a minute ago.
        var board2 = Week(Board(2, FreeTiles(3, 2)));
        var board3 = Week(Board(3, [Tile(3, 0, claimedBy: claimerUserId, claimedAt: DateTimeOffset.UtcNow.AddMinutes(-1)), .. FreeTiles(8, 3)]));
        ReturnBoards(host, clubId, board2, board3, board3);

        await host.SendAsync(new NotifyMissionBoardChangesCommand(clubId)); // first look: stored only
        await host.SendAsync(new NotifyMissionBoardChangesCommand(clubId)); // board 3 unlocked
        await host.SendAsync(new NotifyMissionBoardChangesCommand(clubId)); // nothing new

        await messages.Received(2).SendAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await messages.Received(1).SendAsync(always,
            Arg.Is<string>(s => s.Contains("Board 3") && s.Contains("8 missions") && s.Contains($"club-{clubId:N}")),
            true, Arg.Any<CancellationToken>());
        await messages.Received(1).SendAsync(once,
            Arg.Is<string>(s => s.Contains("one-time alert")), false, Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var left = await read.MissionBoardSubscriptions.AsNoTracking()
            .Where(s => new[] { always, once, claimer }.Contains(s.DiscordUserId))
            .Select(s => s.DiscordUserId)
            .ToListAsync();
        left.Should().BeEquivalentTo([always, claimer], "the one-time subscription ended with its DM, the skipped one did not");
        (await read.ClubMissionBoardObservations.AsNoTracking().SingleAsync(o => o.ClubId == clubId))
            .CurrentBoardNumber.Should().Be(3);
    }

    [Fact]
    public async Task NewWeek_IsTold_AndAMemberWhoLeftTheServerIsUnsubscribed()
    {
        var clubId = Guid.NewGuid();
        await SeedClubAsync(clubId);
        var (_, member) = await SeedMemberAsync(clubId);
        using var host = CreateHost(clubId);
        var messages = host.Mock<IMissionBoardChangeMessages>();
        messages.SendAsync(member, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.NotFound(DiscordDmErrorCodes.NoMutualGuild, "gone")));
        await host.SendAsync(new SubscribeToMissionBoardCommand(member, MissionBoardSubscriptionMode.Always));

        var lastWeek = Week(Board(5, FreeTiles(2, 5)));
        var thisWeek = Week(Board(1, FreeTiles(9))) with { PeriodStart = PeriodStart.AddDays(7), PeriodEnd = PeriodStart.AddDays(14) };
        ReturnBoards(host, clubId, lastWeek, thisWeek);

        await host.SendAsync(new NotifyMissionBoardChangesCommand());
        await host.SendAsync(new NotifyMissionBoardChangesCommand());

        await messages.Received(1).SendAsync(member,
            Arg.Is<string>(s => s.Contains("new week") && s.Contains("9 missions")), true, Arg.Any<CancellationToken>());
        (await host.SendAsync(new GetMissionBoardSubscriptionQuery(member))).Should().BeNull();
    }
}
