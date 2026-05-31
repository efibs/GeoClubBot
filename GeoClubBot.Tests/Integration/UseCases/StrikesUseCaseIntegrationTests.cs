using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UseCases.UseCases.Strikes;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the strike use cases through the real MediatR pipeline (validation + unit-of-work +
/// EF repositories) against the shared Postgres container. Each test seeds its own Club/User
/// namespace so the shared container is reused safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StrikesUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    /// <summary>
    /// Builds a host with a concrete <see cref="ActivityCheckerConfiguration"/> (for the decay
    /// window) and an <em>empty-club</em> <see cref="GeoGuessrConfiguration"/> so the read-or-sync
    /// path used by <see cref="AddStrikeCommand"/> short-circuits to "not found" instead of calling
    /// the (substituted) GeoGuessr API when a member is missing.
    /// </summary>
    private MediatorTestHost CreateHost(TimeSpan? strikeDecay = null) =>
        new(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new ActivityCheckerConfiguration
            {
                Schedule = "0 0 0 * * ?",
                TextChannelId = 1UL,
                MinXP = 100,
                GracePeriodDays = 7,
                MaxNumStrikes = 3,
                HistoryKeepTimeSpan = TimeSpan.FromDays(90),
                StrikeDecayTimeSpan = strikeDecay ?? TimeSpan.FromDays(30),
            }));
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [],
            }));
        });

    private async Task<(Guid clubId, string userId, string nickname)> SeedMemberAsync()
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();
        var nickname = NewNickname();

        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
        await seed.SaveChangesAsync();

        return (clubId, userId, nickname);
    }

    // ---- AddStrike --------------------------------------------------------

    [Fact]
    public async Task AddStrike_PersistsAStrikeForAnExistingMember()
    {
        var (_, userId, nickname) = await SeedMemberAsync();
        var strikeDate = DateTimeOffset.UtcNow.AddDays(-1);

        using var host = CreateHost();
        var result = await host.SendAsync(new AddStrikeCommand(nickname, strikeDate));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);
        (await repo.ReadForUpdateByIdAsync(result.Value)).Should().NotBeNull();
        (await repo.ReadNumberOfActiveStrikesByMemberUserIdAsync(userId)).Should().Be(1);
    }

    [Fact]
    public async Task AddStrike_ReturnsNotFound_WhenMemberDoesNotExist()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new AddStrikeCommand($"missing-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("club_member.not_found");
    }

    // ---- Revoke / Unrevoke ------------------------------------------------

    [Fact]
    public async Task RevokeStrike_MarksTheStrikeRevoked()
    {
        var (_, userId, _) = await SeedMemberAsync();
        var strike = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(strike);
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        var result = await host.SendAsync(new RevokeStrikeCommand(strike.StrikeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Revoked.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfStrikesRepository(read).ReadForUpdateByIdAsync(strike.StrikeId);
        persisted!.Revoked.Should().BeTrue("the revoke must be committed by the unit-of-work behavior");
    }

    [Fact]
    public async Task RevokeStrike_ReturnsNotFound_ForUnknownId()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new RevokeStrikeCommand(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("strike.not_found");
    }

    [Fact]
    public async Task UnrevokeStrike_ClearsTheRevokedFlag()
    {
        var (_, userId, _) = await SeedMemberAsync();
        var strike = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
        strike.Revoke();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(strike);
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        var result = await host.SendAsync(new UnrevokeStrikeCommand(strike.StrikeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Revoked.Should().BeFalse();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfStrikesRepository(read).ReadForUpdateByIdAsync(strike.StrikeId);
        persisted!.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task UnrevokeStrike_ReturnsNotFound_ForUnknownId()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new UnrevokeStrikeCommand(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ---- Read queries -----------------------------------------------------

    [Fact]
    public async Task ReadAllStrikes_IncludesSeededStrikes()
    {
        var (_, userId, _) = await SeedMemberAsync();
        var strike = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(strike);
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        var all = await host.SendAsync(new ReadAllStrikesQuery());

        all.Should().Contain(s => s.StrikeId == strike.StrikeId);
    }

    [Fact]
    public async Task ReadMemberStrikes_ReturnsStatus_ForKnownMember()
    {
        var (_, userId, nickname) = await SeedMemberAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-2)));
            var revoked = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
            revoked.Revoke();
            seed.Add(revoked);
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        var result = await host.SendAsync(new ReadMemberStrikesQuery(nickname));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ReadMemberStrikes_ReturnsNotFound_ForUnknownNickname()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new ReadMemberStrikesQuery($"missing-{Guid.NewGuid():N}"[..20]));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ---- CheckStrikeDecay -------------------------------------------------

    [Fact]
    public async Task CheckStrikeDecay_DeletesStrikesOlderThanTheDecayWindow_AndKeepsRecentOnes()
    {
        var (_, userId, _) = await SeedMemberAsync();
        var decay = TimeSpan.FromDays(30);

        var old = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow - decay - TimeSpan.FromDays(5));
        var recent = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow - decay + TimeSpan.FromDays(5));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(old);
            seed.Add(recent);
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(decay);
        await host.SendAsync(new CheckStrikeDecayCommand());

        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);
        (await repo.ReadForUpdateByIdAsync(old.StrikeId)).Should().BeNull("it is older than the decay window");
        (await repo.ReadForUpdateByIdAsync(recent.StrikeId)).Should().NotBeNull("it is inside the decay window");
    }

    [Fact]
    public async Task CheckStrikeDecay_KeepsAllStrikes_WhenNoneAreOldEnough()
    {
        var (_, userId, _) = await SeedMemberAsync();
        var strike = ClubMemberStrike.Create(userId, DateTimeOffset.UtcNow.AddDays(-1));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(strike);
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(TimeSpan.FromDays(60));
        await host.SendAsync(new CheckStrikeDecayCommand());

        await using var read = fixture.CreateDbContext();
        (await new EfStrikesRepository(read).ReadForUpdateByIdAsync(strike.StrikeId)).Should().NotBeNull();
    }
}
