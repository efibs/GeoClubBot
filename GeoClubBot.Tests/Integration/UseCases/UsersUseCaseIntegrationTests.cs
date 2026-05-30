using Entities;
using FluentAssertions;
using UseCases.UseCases.Users;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the user-projection use case that maps GeoGuessr user ids to the Discord ids of the
/// linked accounts, ignoring unlinked users.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UsersUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    [Fact]
    public async Task GeoGuessrUserIdsToDiscordUserIds_ReturnsOnlyLinkedUsersDiscordIds()
    {
        var linkedUserId = NewUserId();
        var linkedDiscordId = NewDiscordId();
        var unlinkedUserId = NewUserId();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(GeoGuessrUser.Create(linkedUserId, NewNickname(), linkedDiscordId));
            seed.Add(GeoGuessrUser.Create(unlinkedUserId, NewNickname()));
            await seed.SaveChangesAsync();
        }

        using var host = new MediatorTestHost(fixture.ConnectionString);
        var discordIds = await host.SendAsync(new GeoGuessrUserIdsToDiscordUserIdsQuery(
            [linkedUserId, unlinkedUserId]));

        discordIds.Should().ContainSingle().Which.Should().Be(linkedDiscordId);
    }

    [Fact]
    public async Task GeoGuessrUserIdsToDiscordUserIds_ReturnsEmpty_ForNoLinkedUsers()
    {
        var unlinkedUserId = NewUserId();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(GeoGuessrUser.Create(unlinkedUserId, NewNickname()));
            await seed.SaveChangesAsync();
        }

        using var host = new MediatorTestHost(fixture.ConnectionString);
        var discordIds = await host.SendAsync(new GeoGuessrUserIdsToDiscordUserIdsQuery([unlinkedUserId]));

        discordIds.Should().BeEmpty();
    }
}
