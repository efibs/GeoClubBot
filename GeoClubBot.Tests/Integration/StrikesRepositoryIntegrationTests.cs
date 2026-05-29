using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters;
using Xunit;

namespace GeoClubBot.Tests.Integration;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StrikesRepositoryIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];

    private async Task<(Guid clubId, string userId, string nickname)> SeedMemberAsync(Guid clubId)
    {
        var userId = NewUserId();
        var nickname = $"nick-{Guid.NewGuid():N}"[..30];

        await using var seed = fixture.CreateDbContext();
        if (!seed.Clubs.Any(c => c.ClubId == clubId))
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        }
        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
        await seed.SaveChangesAsync();

        return (clubId, userId, nickname);
    }

    [Fact]
    public async Task ReadNumberOfActiveStrikesByMemberUserIdAsync_CountsOnlyActive()
    {
        var (_, userId, _) = await SeedMemberAsync(Guid.NewGuid());

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-3)));
            seed.Add(ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-2)));
            var revoked = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
            revoked.Revoke();
            seed.Add(revoked);
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);

        var count = await repo.ReadNumberOfActiveStrikesByMemberUserIdAsync(userId);

        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteStrikesBeforeAsync_RemovesOnlyStrikesOlderThanThreshold()
    {
        var (_, userId, _) = await SeedMemberAsync(Guid.NewGuid());
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);

        var old = ClubMemberStrike.Create(userId, threshold.AddDays(-5));
        var recent = ClubMemberStrike.Create(userId, threshold.AddDays(5));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(old);
            seed.Add(recent);
            await seed.SaveChangesAsync();
        }

        await using (var act = fixture.CreateDbContext())
        {
            var deleted = await new EfStrikesRepository(act).DeleteStrikesBeforeAsync(threshold);
            deleted.Should().BeGreaterThanOrEqualTo(1);
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);
        (await repo.ReadForUpdateByIdAsync(old.StrikeId)).Should().BeNull();
        (await repo.ReadForUpdateByIdAsync(recent.StrikeId)).Should().NotBeNull();
    }

    [Fact]
    public async Task ReadStrikesByMemberNicknameAsync_ReturnsAllStrikesForMember()
    {
        var (_, userId, nickname) = await SeedMemberAsync(Guid.NewGuid());

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-3)));
            var revoked = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
            revoked.Revoke();
            seed.Add(revoked);
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);

        var strikes = await repo.ReadStrikesByMemberNicknameAsync(nickname);

        strikes.Should().NotBeNull();
        strikes!.Should().HaveCount(2, "the nickname lookup returns both active and revoked strikes");
    }

    [Fact]
    public async Task ReadStrikesByMemberNicknameAsync_ReturnsNull_ForUnknownNickname()
    {
        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);

        var strikes = await repo.ReadStrikesByMemberNicknameAsync($"missing-{Guid.NewGuid():N}");

        strikes.Should().BeNull();
    }
}
