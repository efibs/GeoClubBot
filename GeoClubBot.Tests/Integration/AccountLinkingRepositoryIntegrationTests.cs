using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoClubBot.Tests.Integration;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountLinkingRepositoryIntegrationTests(PostgresFixture fixture)
{
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);
    private static string NewGeoGuessrId() => Guid.NewGuid().ToString("N")[..24];

    [Fact]
    public async Task AddAndReadByDiscordId_RoundTrips()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();

        await using (var seed = fixture.CreateDbContext())
        {
            new EfAccountLinkingRequestRepository(seed)
                .AddRequest(GeoGuessrAccountLinkingRequest.Create(discordId, geoGuessrId, "otp-123"));
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfAccountLinkingRequestRepository(read);

        var byDiscord = await repo.ReadRequestAsync(discordId);
        var byBoth = await repo.ReadRequestAsync(discordId, geoGuessrId);

        byDiscord.Should().NotBeNull();
        byDiscord!.OneTimePassword.Should().Be("otp-123");
        byBoth.Should().NotBeNull();
        byBoth!.GeoGuessrUserId.Should().Be(geoGuessrId);
    }

    [Fact]
    public async Task ReadByBoth_ReturnsNull_WhenGeoGuessrIdDoesNotMatch()
    {
        var discordId = NewDiscordId();

        await using (var seed = fixture.CreateDbContext())
        {
            new EfAccountLinkingRequestRepository(seed)
                .AddRequest(GeoGuessrAccountLinkingRequest.Create(discordId, NewGeoGuessrId(), "otp"));
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfAccountLinkingRequestRepository(read);

        var result = await repo.ReadRequestAsync(discordId, "different-id-000000000001");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRequest_RemovesTheRequest()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();

        await using (var seed = fixture.CreateDbContext())
        {
            new EfAccountLinkingRequestRepository(seed)
                .AddRequest(GeoGuessrAccountLinkingRequest.Create(discordId, geoGuessrId, "otp"));
            await seed.SaveChangesAsync();
        }

        await using (var act = fixture.CreateDbContext())
        {
            // Read on the tracking DbSet (no AsNoTracking) so EF can delete the entity.
            var tracked = await act.GeoGuessrAccountLinkingRequests
                .FirstOrDefaultAsync(r => r.DiscordUserId == discordId);
            new EfAccountLinkingRequestRepository(act).DeleteRequest(tracked!);
            await act.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var result = await new EfAccountLinkingRequestRepository(read).ReadRequestAsync(discordId);

        result.Should().BeNull();
    }
}
