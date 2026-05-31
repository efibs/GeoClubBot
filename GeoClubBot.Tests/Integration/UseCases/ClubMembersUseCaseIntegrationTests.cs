using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UseCases.UseCases.ClubMembers;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the club-member sync use cases (SaveClubMembers upsert + ReadOrSync lookup) through the
/// real MediatR pipeline. New members raise a PlayerJoinedClubEvent which fans out to the (substituted,
/// best-effort) role / private-channel notification handlers; the test asserts the persisted state.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubMembersUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    private MediatorTestHost CreateHost() => new(fixture.ConnectionString);

    private MediatorTestHost CreateHostWithoutClubs() =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [],
            })));

    private async Task<Guid> SeedClubAsync()
    {
        var clubId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        await seed.SaveChangesAsync();
        return clubId;
    }

    [Fact]
    public async Task SaveClubMembers_CreatesTheUserAndMember()
    {
        var clubId = await SeedClubAsync();
        var userId = NewUserId();
        var nickname = NewNickname();
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubId, 500, DateTimeOffset.UtcNow.AddMonths(-3));

        using var host = CreateHost();
        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member.Should().NotBeNull();
        member!.Xp.Should().Be(500);
        member.ClubId.Should().Be(clubId);
        member.User.Nickname.Should().Be(nickname);
    }

    [Fact]
    public async Task SaveClubMembers_UpdatesAnExistingMember()
    {
        var clubId = await SeedClubAsync();
        var userId = NewUserId();
        var nickname = NewNickname();

        await using (var seed = fixture.CreateDbContext())
        {
            var user = GeoGuessrUser.Create(userId, nickname);
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 100, joinedAt: DateTimeOffset.UtcNow.AddMonths(-3)));
            await seed.SaveChangesAsync();
        }

        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubId, 250, DateTimeOffset.UtcNow.AddMonths(-3));

        using var host = CreateHost();
        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.Xp.Should().Be(250, "the existing member's XP should be synced to the snapshot value");
    }

    [Fact]
    public async Task ReadOrSyncClubMember_ByNickname_ReturnsTheExistingMember()
    {
        var clubId = await SeedClubAsync();
        var userId = NewUserId();
        var nickname = NewNickname();
        await using (var seed = fixture.CreateDbContext())
        {
            var user = GeoGuessrUser.Create(userId, nickname);
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        var result = await host.SendAsync(new ReadOrSyncClubMemberByNicknameQuery(nickname));

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task ReadOrSyncClubMember_ByUserId_ReturnsTheExistingMember()
    {
        var clubId = await SeedClubAsync();
        var userId = NewUserId();
        await using (var seed = fixture.CreateDbContext())
        {
            var user = GeoGuessrUser.Create(userId, NewNickname());
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        var result = await host.SendAsync(new ReadOrSyncClubMemberByUserIdQuery(userId));

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task ReadOrSyncClubMember_ReturnsNotFound_WhenAbsentAndNoClubsConfigured()
    {
        using var host = CreateHostWithoutClubs();

        var result = await host.SendAsync(new ReadOrSyncClubMemberByNicknameQuery($"missing-{Guid.NewGuid():N}"[..20]));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("club_member.not_found");
    }
}
