using Configuration;
using Entities;
using FluentAssertions;
using FluentValidation;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UseCases.UseCases.Excuses;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the excuse use-case handlers through the real MediatR pipeline (validation +
/// unit-of-work + EF repositories) against the shared Postgres container. Each test seeds its own
/// Club/User namespace so the container is reused safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ExcusesUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    private MediatorTestHost CreateHost() => new(fixture.ConnectionString);

    /// <summary>
    /// Host whose read-or-sync path short-circuits to "not found" (empty club list) instead of
    /// hitting the substituted GeoGuessr API when a member is missing.
    /// </summary>
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

    private async Task<string> SeedMemberAsync()
    {
        var clubId = Guid.NewGuid();
        var nickname = NewNickname();
        var userId = NewUserId();

        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
        await seed.SaveChangesAsync();

        return nickname;
    }

    private async Task<(string nickname, ClubMemberExcuse excuse)> SeedMemberWithExcuseAsync(
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

    // ---- AddExcuse --------------------------------------------------------

    [Fact]
    public async Task AddExcuse_PersistsAnExcuseForAnExistingMember()
    {
        var nickname = await SeedMemberAsync();
        var from = DateTimeOffset.UtcNow.AddDays(1);
        var to = DateTimeOffset.UtcNow.AddDays(5);

        using var host = CreateHost();
        var result = await host.SendAsync(new AddExcuseCommand(nickname, from, to));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfExcusesRepository(read).ReadExcuseAsync(result.Value);
        persisted.Should().NotBeNull();
        persisted!.From.Should().BeCloseTo(from, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AddExcuse_ReturnsNotFound_WhenMemberDoesNotExist()
    {
        using var host = CreateHostWithoutClubs();

        var result = await host.SendAsync(new AddExcuseCommand(
            $"missing-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2)));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("club_member.not_found");
    }

    // ---- UpdateExcuse -----------------------------------------------------

    [Fact]
    public async Task UpdateExcuse_PersistsNewTimeRange()
    {
        var (_, excuse) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
        var newFrom = DateTimeOffset.UtcNow.AddDays(10);
        var newTo = DateTimeOffset.UtcNow.AddDays(20);

        using var host = CreateHost();
        var result = await host.SendAsync(new UpdateExcuseCommand(excuse.ExcuseId, newFrom, newTo));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfExcusesRepository(read).ReadExcuseAsync(excuse.ExcuseId);
        persisted.Should().NotBeNull();
        persisted!.From.Should().BeCloseTo(newFrom, TimeSpan.FromSeconds(1));
        persisted.To.Should().BeCloseTo(newTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdateExcuse_ReturnsNotFound_WhenExcuseMissing()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new UpdateExcuseCommand(
            Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("excuse.not_found");
    }

    // ---- RemoveExcuse -----------------------------------------------------

    [Fact]
    public async Task RemoveExcuse_DeletesItFromTheDatabase()
    {
        var (_, excuse) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));

        using var host = CreateHost();
        var result = await host.SendAsync(new RemoveExcuseCommand(excuse.ExcuseId));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfExcusesRepository(read).ReadExcuseAsync(excuse.ExcuseId);
        persisted.Should().BeNull("the excuse should have been deleted and the unit of work committed");
    }

    [Fact]
    public async Task RemoveExcuse_ReturnsNotFound_WhenExcuseMissing()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new RemoveExcuseCommand(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ---- ReadExcuses ------------------------------------------------------

    [Fact]
    public async Task ReadExcuses_ByNickname_ReturnsOnlyThatMembersExcuses()
    {
        var (nickname, excuse) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));

        using var host = CreateHost();
        var excuses = await host.SendAsync(new ReadExcusesQuery(nickname));

        excuses.Should().ContainSingle();
        excuses[0].ExcuseId.Should().Be(excuse.ExcuseId);
    }

    [Fact]
    public async Task ReadExcuses_WithoutNickname_ReturnsSeededExcuse()
    {
        var (_, excuse) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));

        using var host = CreateHost();
        var excuses = await host.SendAsync(new ReadExcusesQuery());

        excuses.Should().Contain(e => e.ExcuseId == excuse.ExcuseId);
    }

    // ---- ReadRelevantExcuses -----------------------------------------------

    [Fact]
    public async Task ReadRelevantExcuses_ReturnsActiveExcuse_ThroughFullPipeline()
    {
        var (nickname, _) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(4));

        using var host = CreateHost();
        var result = await host.SendAsync(new ReadRelevantExcusesQuery(7));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.MemberNickname == nickname && !r.IsUpcoming);
    }

    [Fact]
    public async Task ReadRelevantExcuses_ReturnsUpcomingExcuse_ThroughFullPipeline()
    {
        var (nickname, _) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(9));

        using var host = CreateHost();
        var result = await host.SendAsync(new ReadRelevantExcusesQuery(7));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.MemberNickname == nickname && r.IsUpcoming);
    }

    [Fact]
    public async Task ReadRelevantExcuses_DoesNotReturnPastExcuse()
    {
        var (nickname, _) = await SeedMemberWithExcuseAsync(
            DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(-1));

        using var host = CreateHost();
        var result = await host.SendAsync(new ReadRelevantExcusesQuery(7));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.MemberNickname == nickname,
            "the excuse ended before now");
    }

    [Fact]
    public async Task ReadRelevantExcuses_ThrowsValidationException_WhenUpcomingDaysIsZero()
    {
        using var host = CreateHost();

        var act = async () => await host.SendAsync(new ReadRelevantExcusesQuery(0));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ReadRelevantExcuses_ThrowsValidationException_WhenUpcomingDaysIsNegative()
    {
        using var host = CreateHost();

        var act = async () => await host.SendAsync(new ReadRelevantExcusesQuery(-3));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
