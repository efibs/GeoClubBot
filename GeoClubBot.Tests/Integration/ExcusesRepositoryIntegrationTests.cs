using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
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

    // ---- ReadAllRelevantExcusesAsync ---------------------------------------

    private async Task<(string Nickname, ClubMemberExcuse Excuse)> SeedExcuseAsync(
        DateTimeOffset from, DateTimeOffset to)
    {
        var clubId = Guid.NewGuid();
        var nickname = NewNickname();
        var userId = NewUserId();
        var excuse = ClubMemberExcuse.Create(userId, from, to);

        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        var member = ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1));
        member.Excuses.Add(excuse);
        seed.Add(member);
        await seed.SaveChangesAsync();

        return (nickname, excuse);
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ReturnsActiveExcuse_WithIsUpcomingFalse()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-2);
        var to = DateTimeOffset.UtcNow.AddDays(3);
        var (nickname, _) = await SeedExcuseAsync(from, to);

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        var match = results.SingleOrDefault(r => r.MemberNickname == nickname);
        match.Should().NotBeNull();
        match!.IsUpcoming.Should().BeFalse("the excuse has already started");
        match.ExcuseTimeRange.From.Should().BeCloseTo(from, TimeSpan.FromSeconds(1));
        match.ExcuseTimeRange.To.Should().BeCloseTo(to, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ReturnsUpcomingExcuse_WithIsUpcomingTrue()
    {
        var (nickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(10));

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        var match = results.SingleOrDefault(r => r.MemberNickname == nickname);
        match.Should().NotBeNull();
        match!.IsUpcoming.Should().BeTrue("the excuse has not started yet");
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ExcludesPastExcuse()
    {
        var (nickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(-1));

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        results.Should().NotContain(r => r.MemberNickname == nickname,
            "the excuse ended before now");
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ExcludesFarFutureExcuse_BeyondThreshold()
    {
        var (nickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(14), DateTimeOffset.UtcNow.AddDays(20));

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        results.Should().NotContain(r => r.MemberNickname == nickname,
            "the excuse starts more than 7 days in the future");
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_IncludesUpcomingExcuse_StartingJustBeforeThreshold()
    {
        // An excuse starting just inside the threshold window must be included.
        var (nickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(6).AddHours(23), DateTimeOffset.UtcNow.AddDays(14));

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        results.Should().Contain(r => r.MemberNickname == nickname,
            "the excuse starts before the 7-day threshold");
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ExcludesExcuse_StartingJustAfterThreshold()
    {
        // An excuse starting just outside the threshold window must be excluded.
        var (nickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(7).AddMinutes(5), DateTimeOffset.UtcNow.AddDays(14));

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        results.Should().NotContain(r => r.MemberNickname == nickname,
            "the excuse starts after the 7-day threshold");
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ReturnsActiveAndUpcoming_ForMultipleMembers()
    {
        var (activeNickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(4));
        var (upcomingNickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(8));

        await using var read = fixture.CreateDbContext();
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(7);

        results.Should().Contain(r => r.MemberNickname == activeNickname, "active excuse is relevant");
        results.Should().Contain(r => r.MemberNickname == upcomingNickname, "upcoming excuse is within threshold");
    }

    [Fact]
    public async Task ReadAllRelevantExcusesAsync_ReturnsEmpty_WhenNoRelevantExcusesExist()
    {
        // Seed only a past and a far-future excuse so neither is relevant.
        var (pastNickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-2));
        var (futurNickname, _) = await SeedExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow.AddDays(40));

        await using var read = fixture.CreateDbContext();

        // Use a very small threshold so the far-future excuse is definitely excluded.
        var results = await new EfExcusesRepository(read).ReadAllRelevantExcusesAsync(1);

        results.Should().NotContain(r => r.MemberNickname == pastNickname, "the past excuse ended before now");
        results.Should().NotContain(r => r.MemberNickname == futurNickname, "the far-future excuse starts after the threshold");
    }
}
