using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Integration;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubMemberRepositoryIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];

    [Fact]
    public async Task ReadClubMemberByNicknameAsync_FindsMember_AndEagerLoadsUser()
    {
        var clubId = Guid.NewGuid();
        var nickname = $"nick-{Guid.NewGuid():N}"[..30];
        var userId = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
            var user = GeoGuessrUser.Create(userId, nickname);
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 42, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);

        var member = await repo.ReadClubMemberByNicknameAsync(nickname);

        member.Should().NotBeNull();
        member!.UserId.Should().Be(userId);
        member.Xp.Should().Be(42);
        member.User.Nickname.Should().Be(nickname);
    }

    [Fact]
    public async Task ReadClubMemberByUserIdAsync_ReturnsNull_WhenMissing()
    {
        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);

        var member = await repo.ReadClubMemberByUserIdAsync(NewUserId());

        member.Should().BeNull();
    }

    [Fact]
    public async Task ReadClubMembersByClubIdAsync_ReturnsOnlyMembersOfThatClub()
    {
        var clubId = Guid.NewGuid();
        var otherClubId = Guid.NewGuid();
        var inClub1 = NewUserId();
        var inClub2 = NewUserId();
        var inOtherClub = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
            seed.Add(Club.Create(otherClubId, $"other-{otherClubId:N}", 1));

            foreach (var (userId, club) in new[] { (inClub1, clubId), (inClub2, clubId), (inOtherClub, otherClubId) })
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
                seed.Add(user);
                seed.Add(ClubMember.Create(user, club, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
            }

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);

        var members = await repo.ReadClubMembersByClubIdAsync(clubId);

        members.Select(m => m.UserId).Should().BeEquivalentTo([inClub1, inClub2]);
    }

    [Fact]
    public async Task DeleteClubMembersWithoutHistoryAndStrikesAsync_KeepsMembersWithHistoryOrStrikes()
    {
        var clubId = Guid.NewGuid();
        var orphan = NewUserId();
        var hasHistory = NewUserId();
        var hasStrike = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));

            foreach (var userId in new[] { orphan, hasHistory, hasStrike })
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
                seed.Add(user);
                seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
            }

            seed.Add(ClubMemberHistoryEntry.Create(hasHistory, clubId, xp: 100, DateTimeOffset.UtcNow.AddDays(-1)));
            seed.Add(ClubMemberStrike.Create(hasStrike, DateTimeOffset.UtcNow.AddDays(-1)));

            await seed.SaveChangesAsync();
        }

        await using (var act = fixture.CreateDbContext())
        {
            await new EfClubMemberRepository(act).DeleteClubMembersWithoutHistoryAndStrikesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);
        (await repo.ReadClubMemberByUserIdAsync(orphan)).Should().BeNull("it has neither history nor strikes");
        (await repo.ReadClubMemberByUserIdAsync(hasHistory)).Should().NotBeNull();
        (await repo.ReadClubMemberByUserIdAsync(hasStrike)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteClubMembersWithoutHistoryAndStrikesAsync_KeepsMembersStillHoldingAPrivateChannel()
    {
        var clubId = Guid.NewGuid();
        var hasChannel = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
            var user = GeoGuessrUser.Create(hasChannel, $"nick-{hasChannel}");
            seed.Add(user);
            var member = ClubMember.Create(user, clubId: null, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2));
            member.SetPrivateTextChannelId(4242UL);
            member.ArchivePrivateTextChannel(DateTimeOffset.UtcNow.AddDays(-5));
            seed.Add(member);
            await seed.SaveChangesAsync();
        }

        await using (var act = fixture.CreateDbContext())
        {
            await new EfClubMemberRepository(act).DeleteClubMembersWithoutHistoryAndStrikesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var kept = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(hasChannel);
        kept.Should().NotBeNull("deleting the row would strand the archived Discord channel");
        kept!.PrivateTextChannelId.Should().Be(4242UL);
    }

    [Fact]
    public async Task ReadMembersWithExpiredArchivedPrivateChannelsAsync_OnlyReturnsExpiredArchivesOfClublessMembers()
    {
        var clubId = Guid.NewGuid();
        var expired = NewUserId();
        var fresh = NewUserId();
        var backInAClub = NewUserId();
        var neverArchived = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));

            void AddMember(string userId, Guid? club, ulong channelId, DateTimeOffset? archivedAt)
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
                seed.Add(user);
                var member = ClubMember.Create(user, club, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2));
                member.SetPrivateTextChannelId(channelId);
                if (archivedAt is not null)
                {
                    member.ArchivePrivateTextChannel(archivedAt.Value);
                }
                seed.Add(member);
            }

            AddMember(expired, null, 1UL, DateTimeOffset.UtcNow.AddDays(-40));
            AddMember(fresh, null, 2UL, DateTimeOffset.UtcNow.AddDays(-2));
            AddMember(backInAClub, clubId, 3UL, DateTimeOffset.UtcNow.AddDays(-40));
            AddMember(neverArchived, null, 4UL, null);

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var due = await new EfClubMemberRepository(read)
            .ReadMembersWithExpiredArchivedPrivateChannelsAsync(DateTimeOffset.UtcNow.AddDays(-30));

        due.Select(m => m.UserId).Should().Contain(expired);
        due.Select(m => m.UserId).Should().NotContain([fresh, backInAClub, neverArchived]);
    }
}
