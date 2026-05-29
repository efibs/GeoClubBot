using Infrastructure.OutputAdapters.DataAccess;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeoClubBot.Tests.Integration;

/// <summary>
/// Spins up a fresh Postgres container per test-class collection, applies the production
/// EF Core migrations, and hands out a <see cref="DbContextOptions{T}"/> ready for tests
/// to construct their own <see cref="GeoClubBotDbContext"/> instances. Each test should
/// seed its own data scoped to a unique <c>ClubId</c>/<c>UserId</c> namespace so parallel
/// fixtures don't collide.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("geoclubbot_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public DbContextOptions<GeoClubBotDbContext> DbContextOptions { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        DbContextOptions = new DbContextOptionsBuilder<GeoClubBotDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Tests don't exercise the domain-event dispatch path, so a substituted
    /// <see cref="IMediator"/> is enough to satisfy the ctor.
    /// </summary>
    public GeoClubBotDbContext CreateDbContext() => new(DbContextOptions, Substitute.For<IMediator>());
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
