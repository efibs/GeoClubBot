using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters;
using Xunit;

namespace GeoClubBot.Tests.Integration;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ExcusesRepositoryIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    [Fact]
    public async Task DeleteExcusesBeforeAsync_RemovesOnlyExcusesEndingBeforeThreshold()
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();
        var threshold = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var expired = ClubMemberExcuse.Create(userId, threshold.AddDays(-10), threshold.AddDays(-3));
        var stillActive = ClubMemberExcuse.Create(userId, threshold.AddDays(-1), threshold.AddDays(5));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
            var user = GeoGuessrUser.Create(userId, NewNickname());
            seed.Add(user);
            var member = ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1));
            member.Excuses.Add(expired);
            member.Excuses.Add(stillActive);
            seed.Add(member);
            await seed.SaveChangesAsync();
        }

        await using (var act = fixture.CreateDbContext())
        {
            await new EfExcusesRepository(act).DeleteExcusesBeforeAsync(threshold);
        }

        await using var read = fixture.CreateDbContext();
        var verify = new EfExcusesRepository(read);
        (await verify.ReadExcuseAsync(expired.ExcuseId)).Should().BeNull("its To is before the threshold");
        (await verify.ReadExcuseAsync(stillActive.ExcuseId)).Should().NotBeNull("its To is after the threshold");
    }

    [Fact]
    public async Task ReadExcusesByMemberNicknameAsync_ReturnsExcusesForThatMemberOnly()
    {
        var clubId = Guid.NewGuid();
        var nickname = NewNickname();
        var userId = NewUserId();
        var otherUserId = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));

            var user = GeoGuessrUser.Create(userId, nickname);
            seed.Add(user);
            var member = ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1));
            member.Excuses.Add(ClubMemberExcuse.Create(userId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2)));
            member.Excuses.Add(ClubMemberExcuse.Create(userId, DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(5)));
            seed.Add(member);

            var otherUser = GeoGuessrUser.Create(otherUserId, NewNickname());
            seed.Add(otherUser);
            var otherMember = ClubMember.Create(otherUser, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1));
            otherMember.Excuses.Add(ClubMemberExcuse.Create(otherUserId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2)));
            seed.Add(otherMember);

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfExcusesRepository(read);

        var excuses = await repo.ReadExcusesByMemberNicknameAsync(nickname);

        excuses.Should().HaveCount(2);
        excuses.Should().OnlyContain(e => e.UserId == userId);
    }

    [Fact]
    public async Task ReadExcusesByMemberNicknameAsync_ReturnsEmpty_ForUnknownNickname()
    {
        await using var read = fixture.CreateDbContext();
        var repo = new EfExcusesRepository(read);

        var excuses = await repo.ReadExcusesByMemberNicknameAsync($"missing-{Guid.NewGuid():N}");

        excuses.Should().BeEmpty();
    }
}
