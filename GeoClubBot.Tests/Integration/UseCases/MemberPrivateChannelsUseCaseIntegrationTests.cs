using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the member-private-channel use cases (create / delete) through the real MediatR
/// pipeline. The Discord channel access is substituted so the test controls whether the channel
/// create/delete "succeeds" and asserts the persisted PrivateTextChannelId.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MemberPrivateChannelsUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private const ulong LiveCategoryId = 42UL;
    private const ulong ArchiveCategoryId = 43UL;

    private MediatorTestHost CreateHost(TimeSpan? archiveKeep = null) =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["MemberPrivateChannels:CategoryId"] = LiveCategoryId.ToString(),
            ["MemberPrivateChannels:Description"] = "Private channels",
            ["MemberPrivateChannels:ArchiveCategoryId"] = ArchiveCategoryId.ToString(),
            ["MemberPrivateChannels:ArchiveKeepTimeSpan"] = (archiveKeep ?? TimeSpan.FromDays(30)).ToString(),
            ["MemberPrivateChannels:ArchiveCleanupSchedule"] = "0 0 3 * * ?",
        });

    private async Task<ClubMember> SeedLinkedMemberAsync(ulong? privateChannelId = null,
        DateTimeOffset? archivedAt = null, bool inClub = true)
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();

        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        var user = GeoGuessrUser.Create(userId, NewNickname(), NewDiscordId());
        seed.Add(user);
        var member = ClubMember.Create(user, inClub ? clubId : null, xp: 0,
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

    [Fact]
    public async Task CreatePrivateChannel_PersistsTheCreatedChannelId()
    {
        var member = await SeedLinkedMemberAsync();

        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .CreatePrivateTextChannelAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<ulong>?>(), Arg.Any<IEnumerable<ulong>?>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)888UL);

        var result = await host.SendAsync(new CreateMemberPrivateChannelCommand(member));

        result.Should().Be(888UL);

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().Be(888UL);
    }

    [Fact]
    public async Task DeletePrivateChannel_ClearsTheChannelId_WhenDeletionSucceeds()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .DeleteTextChannelAsync(555UL, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await host.SendAsync(new DeleteMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().BeNull();
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
        host.Mock<IDiscordTextChannelAccess>()
            .UpdateTextChannelAsync(Arg.Any<TextChannel>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await host.SendAsync(new ArchiveMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();

        await host.Mock<IDiscordTextChannelAccess>().Received(1).UpdateTextChannelAsync(
            Arg.Is<TextChannel>(c => c.Id == 555UL && c.CategoryId == ArchiveCategoryId),
            Arg.Any<CancellationToken>());
        await host.Mock<IDiscordMessageAccess>().Received(1)
            .SendMessageAsync(Arg.Any<string>(), 555UL, Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().Be(555UL);
        persisted.PrivateTextChannelArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchivePrivateChannel_LeavesTheTimestampAlone_WhenTheDiscordMoveFails()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .UpdateTextChannelAsync(Arg.Any<TextChannel>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await host.SendAsync(new ArchiveMemberPrivateChannelCommand(member));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("member_private_channel.archive_failed");

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task RestorePrivateChannel_MovesTheChannelBack_RenamesIt_AndClearsTheTimestamp()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL,
            archivedAt: DateTimeOffset.UtcNow.AddDays(-2));

        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .UpdateTextChannelAsync(Arg.Any<TextChannel>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await host.SendAsync(new RestoreMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();

        var expectedName = $"{member.User.Nickname.ToLowerInvariant()}-private-channel";
        await host.Mock<IDiscordTextChannelAccess>().Received(1).UpdateTextChannelAsync(
            Arg.Is<TextChannel>(c => c.Id == 555UL && c.CategoryId == LiveCategoryId && c.Name == expectedName),
            Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().Be(555UL);
        persisted.PrivateTextChannelArchivedAt.Should().BeNull();
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
        host.Mock<IDiscordTextChannelAccess>()
            .DeleteTextChannelAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await host.SendAsync(new DeleteExpiredArchivedPrivateChannelsCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        await host.Mock<IDiscordTextChannelAccess>().Received(1)
            .DeleteTextChannelAsync(777UL, Arg.Any<CancellationToken>());
        await host.Mock<IDiscordTextChannelAccess>().DidNotReceive()
            .DeleteTextChannelAsync(778UL, Arg.Any<CancellationToken>());
        await host.Mock<IDiscordTextChannelAccess>().DidNotReceive()
            .DeleteTextChannelAsync(779UL, Arg.Any<CancellationToken>());

        await using var read = fixture.CreateDbContext();
        var repository = new EfClubMemberRepository(read);
        (await repository.ReadClubMemberByUserIdAsync(expired.UserId))!.PrivateTextChannelId.Should().BeNull();
        (await repository.ReadClubMemberByUserIdAsync(fresh.UserId))!.PrivateTextChannelId.Should().Be(778UL);
        (await repository.ReadClubMemberByUserIdAsync(live.UserId))!.PrivateTextChannelId.Should().Be(779UL);
    }
}
