using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestDoubles;
using Infrastructure.OutputAdapters.Repositories;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the member-private-channel use cases through the real MediatR pipeline and database,
/// against the host's in-memory Discord server, asserting the persisted channel ids, timestamps and
/// category rows.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MemberPrivateChannelsUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private const ulong LegacyCategoryId = 42UL;
    private const ulong ArchiveCategoryId = 43UL;

    private MediatorTestHost CreateHost(TimeSpan? archiveKeep = null) =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["MemberPrivateChannels:Description"] = "Private channels",
            ["MemberPrivateChannels:ArchiveCategoryId"] = ArchiveCategoryId.ToString(),
            ["MemberPrivateChannels:ArchiveKeepTimeSpan"] = (archiveKeep ?? TimeSpan.FromDays(30)).ToString(),
            ["MemberPrivateChannels:ArchiveCleanupSchedule"] = "0 0 3 * * ?",
        });

    /// <summary>The host's in-memory Discord server, with the archive and the old shared category.</summary>
    private static FakeDiscordChannels Discord(MediatorTestHost host)
    {
        var discord = (FakeDiscordChannels)host.Mock<IDiscordTextChannelAccess>();
        discord.AddCategory(LegacyCategoryId, "private channels");
        discord.AddCategory(ArchiveCategoryId, "archive");
        return discord;
    }

    private async Task<ClubMember> SeedLinkedMemberAsync(ulong? privateChannelId = null,
        DateTimeOffset? archivedAt = null, bool inClub = true, Guid? clubId = null)
    {
        var club = clubId ?? Guid.NewGuid();
        var userId = NewUserId();

        await using var seed = fixture.CreateDbContext();
        if (clubId is null)
        {
            seed.Add(Club.Create(club, $"club-{club:N}", 1));
        }
        var user = GeoGuessrUser.Create(userId, NewNickname(), NewDiscordId());
        seed.Add(user);
        var member = ClubMember.Create(user, inClub ? club : null, xp: 0,
            joinedAt: DateTimeOffset.UtcNow.AddMonths(-1));
        if (privateChannelId is not null)
        {
            member.SetPrivateTextChannelId(privateChannelId.Value);

            if (archivedAt is not null)
            {
                member.ArchivePrivateTextChannel(archivedAt.Value);
            }
        }
        seed.Add(member);
        await seed.SaveChangesAsync();

        // Reload with the User navigation populated to hand to the command.
        await using var read = fixture.CreateDbContext();
        return (await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId))!;
    }

    private async Task<Guid> SeedClubAsync()
    {
        var clubId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        await seed.SaveChangesAsync();
        return clubId;
    }

    private async Task<ClubMember?> ReadAsync(string userId)
    {
        await using var read = fixture.CreateDbContext();
        return await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
    }

    [Fact]
    public async Task CreatePrivateChannel_PersistsTheChannelId_AndTheClubsNewCategory()
    {
        var member = await SeedLinkedMemberAsync();

        using var host = CreateHost();
        var discord = Discord(host);

        var result = await host.SendAsync(new CreateMemberPrivateChannelCommand(member));

        result.Should().NotBeNull();
        (await ReadAsync(member.UserId))!.PrivateTextChannelId.Should().Be(result);

        var category = discord.CategoriesNamed($"club-{member.ClubId:N} – Private").Should().ContainSingle().Subject;
        discord.Channels[result!.Value].Parent.Should().Be(category.Id);

        await using var read = fixture.CreateDbContext();
        var rows = await new EfMemberPrivateChannelCategoryRepository(read).ReadAllAsync();
        rows.Should().ContainSingle(r => r.ClubId == member.ClubId)
            .Which.Should().Match<MemberPrivateChannelCategory>(r => r.CategoryId == category.Id && r.Index == 1);
    }

    [Fact]
    public async Task DeletePrivateChannel_ClearsTheChannelId_WhenDeletionSucceeds()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = CreateHost();
        Discord(host).AddChannel(555UL, LegacyCategoryId);

        var result = await host.SendAsync(new DeleteMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();
        (await ReadAsync(member.UserId))!.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task DeletePrivateChannel_ReturnsNotFound_WhenNoChannelConfigured()
    {
        var member = await SeedLinkedMemberAsync();

        using var host = CreateHost();
        var result = await host.SendAsync(new DeleteMemberPrivateChannelCommand(member));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ArchivePrivateChannel_MovesTheChannelAndStampsTheTimestamp()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = CreateHost();
        var discord = Discord(host);
        discord.AddChannel(555UL, LegacyCategoryId);

        var result = await host.SendAsync(new ArchiveMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();
        discord.Channels[555UL].Parent.Should().Be(ArchiveCategoryId);
        await host.Mock<IDiscordMessageAccess>().Received(1)
            .SendMessageAsync(Arg.Any<string>(), 555UL, Arg.Any<CancellationToken>());

        var persisted = await ReadAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().Be(555UL);
        persisted.PrivateTextChannelArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchivePrivateChannel_LeavesTheTimestampAlone_WhenTheDiscordMoveFails()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = CreateHost();
        var discord = Discord(host);
        discord.AddChannel(555UL, LegacyCategoryId);
        discord.RefuseUpdates = true;

        var result = await host.SendAsync(new ArchiveMemberPrivateChannelCommand(member));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("member_private_channel.archive_failed");
        (await ReadAsync(member.UserId))!.PrivateTextChannelArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task ArchivePrivateChannel_IntoAFullArchive_DeletesTheLongestArchivedChannelForGood()
    {
        var oldest = await SeedLinkedMemberAsync(privateChannelId: 601UL,
            archivedAt: DateTimeOffset.UtcNow.AddYears(-20), inClub: false);
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = new MediatorTestHost(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["MemberPrivateChannels:Description"] = "Private channels",
            ["MemberPrivateChannels:ArchiveCategoryId"] = ArchiveCategoryId.ToString(),
            ["MemberPrivateChannels:ArchiveKeepTimeSpan"] = TimeSpan.FromDays(30).ToString(),
            ["MemberPrivateChannels:ArchiveCleanupSchedule"] = "0 0 3 * * ?",
            ["MemberPrivateChannels:MaxChannelsPerCategory"] = "1",
        });
        var discord = Discord(host);
        discord.AddChannel(555UL, LegacyCategoryId);
        discord.AddChannel(601UL, ArchiveCategoryId);

        var result = await host.SendAsync(new ArchiveMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();
        discord.Channels.Should().NotContainKey(601UL);
        discord.Channels[555UL].Parent.Should().Be(ArchiveCategoryId);
        (await ReadAsync(oldest.UserId))!.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task RestorePrivateChannel_MovesTheChannelIntoTheClubsCategory_RenamesIt_AndClearsTheTimestamp()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL,
            archivedAt: DateTimeOffset.UtcNow.AddDays(-2));

        using var host = CreateHost();
        var discord = Discord(host);
        discord.AddChannel(555UL, ArchiveCategoryId, "stale-name");

        var result = await host.SendAsync(new RestoreMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();

        var category = discord.CategoriesNamed($"club-{member.ClubId:N} – Private").Should().ContainSingle().Subject;
        discord.Channels[555UL].Parent.Should().Be(category.Id);
        discord.Channels[555UL].Name.Should().Be($"{member.User.Nickname.ToLowerInvariant()}-private-channel");

        var persisted = await ReadAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().Be(555UL);
        persisted.PrivateTextChannelArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Reconcile_MigratesTheOldSharedCategory_AndRecoversTheLostChannel()
    {
        var clubId = await SeedClubAsync();
        var inOldCategory = await SeedLinkedMemberAsync(privateChannelId: 555UL, clubId: clubId);
        var lost = await SeedLinkedMemberAsync(clubId: clubId);

        using var host = CreateHost();
        var discord = Discord(host);
        discord.AddChannel(555UL, LegacyCategoryId);

        var dryRun = await host.SendAsync(new ReconcileMemberPrivateChannelsCommand(DryRun: true));

        dryRun.Value.Repairs[PrivateChannelRepair.MoveToClub].Should().Contain(inOldCategory.User.Nickname);
        dryRun.Value.Repairs[PrivateChannelRepair.Create].Should().Contain(lost.User.Nickname);
        discord.Writes.Should().BeEmpty("a dry run changes nothing");
        (await ReadAsync(lost.UserId))!.PrivateTextChannelId.Should().BeNull();

        var result = await host.SendAsync(new ReconcileMemberPrivateChannelsCommand(DryRun: false));

        result.Value.Failures.Should().BeEmpty();
        var category = discord.CategoriesNamed($"club-{clubId:N} – Private").Should().ContainSingle().Subject;
        discord.Channels[555UL].Parent.Should().Be(category.Id);
        discord.Channels[555UL].Access.Should().Contain(
            new ChannelAccess(inOldCategory.User.DiscordUserId!.Value, ChannelAccessTarget.User, ChannelRights.View, ChannelRights.None));

        var recovered = (await ReadAsync(lost.UserId))!.PrivateTextChannelId;
        recovered.Should().NotBeNull();
        discord.Channels[recovered!.Value].Parent.Should().Be(category.Id);

        // Everything else in the shared test database may also be repaired; this run's own members are.
        var again = await host.SendAsync(new ReconcileMemberPrivateChannelsCommand(DryRun: true));
        again.Value.Repairs.Values.SelectMany(n => n)
            .Should().NotContain([inOldCategory.User.Nickname, lost.User.Nickname]);
    }

    [Fact]
    public async Task DeleteExpiredArchives_DeletesThePastRetentionOnesAndLeavesTheRestAlone()
    {
        var expired = await SeedLinkedMemberAsync(privateChannelId: 777UL,
            archivedAt: DateTimeOffset.UtcNow.AddDays(-40), inClub: false);
        var fresh = await SeedLinkedMemberAsync(privateChannelId: 778UL,
            archivedAt: DateTimeOffset.UtcNow.AddDays(-2), inClub: false);
        var live = await SeedLinkedMemberAsync(privateChannelId: 779UL);

        using var host = CreateHost(archiveKeep: TimeSpan.FromDays(30));
        var discord = Discord(host);
        discord.AddChannel(777UL, ArchiveCategoryId);
        discord.AddChannel(778UL, ArchiveCategoryId);
        discord.AddChannel(779UL, LegacyCategoryId);

        var result = await host.SendAsync(new DeleteExpiredArchivedPrivateChannelsCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        discord.Channels.Should().NotContainKey(777UL).And.ContainKeys(778UL, 779UL);

        (await ReadAsync(expired.UserId))!.PrivateTextChannelId.Should().BeNull();
        (await ReadAsync(fresh.UserId))!.PrivateTextChannelId.Should().Be(778UL);
        (await ReadAsync(live.UserId))!.PrivateTextChannelId.Should().Be(779UL);
    }
}
