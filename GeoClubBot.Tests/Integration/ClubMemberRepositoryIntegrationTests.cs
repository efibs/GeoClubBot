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
}
