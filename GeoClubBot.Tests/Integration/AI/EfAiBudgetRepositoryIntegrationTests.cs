using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using UseCases.OutputPorts.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// The daily budget is the only thing standing between the bot and the provider's free-tier ceiling,
/// past which every request is answered with HTTP 429 for the rest of the day. It is therefore
/// exercised against a real PostgreSQL instance: the reservation is a single ON CONFLICT statement
/// whose whole purpose is atomicity, and no in-memory provider can prove that property.
///
/// Tests namespace themselves by using a distinct UTC date per test, so the shared container is
/// reused safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfAiBudgetRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TryReserveRequests_GrantsExactlyTheCap_WhenManyCallersRaceInParallel()
    {
        var date = NewDate();
        const int cap = 10;
        const int contenders = 20;

        // Each task needs its own DbContext: DbContext is not thread-safe, and sharing one here would
        // test EF's concurrency behaviour rather than the database's.
        var attempts = Enumerable.Range(0, contenders)
            .Select(async _ =>
            {
                await using var dbContext = fixture.CreateDbContext();
                var repository = new EfAiBudgetRepository(dbContext);
                return await repository.TryReserveRequestsAsync(date, amount: 1, dailyCap: cap);
            });

        var results = await Task.WhenAll(attempts);

        results.Count(granted => granted).Should().Be(cap,
            "a read-modify-write would let callers interleave and overshoot the provider's allowance");

        var snapshot = await ReadAsync(date);
        snapshot.RequestCount.Should().Be(cap);
    }

    [Fact]
    public async Task TryReserveRequests_RefusesOnceTheCapIsReached()
    {
        var date = NewDate();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new EfAiBudgetRepository(dbContext);

        (await repository.TryReserveRequestsAsync(date, amount: 2, dailyCap: 3)).Should().BeTrue();
        (await repository.TryReserveRequestsAsync(date, amount: 1, dailyCap: 3)).Should().BeTrue();
        (await repository.TryReserveRequestsAsync(date, amount: 1, dailyCap: 3)).Should().BeFalse();

        (await ReadAsync(date)).RequestCount.Should().Be(3, "a refused claim must not increment the counter");
    }

    [Fact]
    public async Task TryReserveRequests_RefusesASingleClaimLargerThanTheCap()
    {
        // Guards the INSERT branch: the day's first row has no existing count for the ON CONFLICT
        // predicate to compare against, so an oversized claim would otherwise be written unchecked.
        var date = NewDate();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new EfAiBudgetRepository(dbContext);

        (await repository.TryReserveRequestsAsync(date, amount: 50, dailyCap: 10)).Should().BeFalse();

        (await ReadAsync(date)).RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseRequests_ReturnsUnspentReservations_WithoutGoingNegative()
    {
        var date = NewDate();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new EfAiBudgetRepository(dbContext);

        await repository.TryReserveRequestsAsync(date, amount: 3, dailyCap: 10);
        await repository.ReleaseRequestsAsync(date, amount: 2);

        (await ReadAsync(date)).RequestCount.Should().Be(1);

        // A double-release must not hand out free budget on subsequent days' arithmetic.
        await repository.ReleaseRequestsAsync(date, amount: 5);
        (await ReadAsync(date)).RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordTokenUsage_AccumulatesAcrossCalls_AndCreatesTheDayIfMissing()
    {
        var date = NewDate();
        await using var dbContext = fixture.CreateDbContext();
        var repository = new EfAiBudgetRepository(dbContext);

        await repository.RecordTokenUsageAsync(date, promptTokens: 100, completionTokens: 40);
        await repository.RecordTokenUsageAsync(date, promptTokens: 25, completionTokens: 10);

        var snapshot = await ReadAsync(date);
        snapshot.PromptTokens.Should().Be(125);
        snapshot.CompletionTokens.Should().Be(50);
        snapshot.RequestCount.Should().Be(0, "token accounting is reporting only and never consumes budget");
    }

    [Fact]
    public async Task Read_ReturnsAnEmptySnapshot_ForADayWithNoActivity()
    {
        var snapshot = await ReadAsync(NewDate());

        snapshot.RequestCount.Should().Be(0);
        snapshot.PromptTokens.Should().Be(0);
        snapshot.CompletionTokens.Should().Be(0);
    }

    private async Task<AiBudgetSnapshot> ReadAsync(DateOnly date)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await new EfAiBudgetRepository(dbContext).ReadAsync(date);
    }

    /// <summary>
    /// A unique date per test. The table is keyed by date, so this is the natural isolation seam and
    /// lets the shared container be reused without cross-test interference.
    /// </summary>
    private static DateOnly NewDate() => DateOnly.FromDayNumber(Random.Shared.Next(1, 3_000_000));
}
