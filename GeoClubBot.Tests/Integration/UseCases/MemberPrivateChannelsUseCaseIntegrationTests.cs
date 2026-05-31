using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the member-private-channel use cases (create / delete) through the real MediatR
/// pipeline. The Discord channel access is substituted so the test controls whether the channel
/// create/delete "succeeds" and asserts the persisted PrivateTextChannelId.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MemberPrivateChannelsUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private MediatorTestHost CreateHost() =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["MemberPrivateChannels:CategoryId"] = "42",
            ["MemberPrivateChannels:Description"] = "Private channels",
        });

    private async Task<ClubMember> SeedLinkedMemberAsync(ulong? privateChannelId = null)
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();

        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        var user = GeoGuessrUser.Create(userId, NewNickname(), NewDiscordId());
        seed.Add(user);
        var member = ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1));
        if (privateChannelId is not null)
        {
            member.SetPrivateTextChannelId(privateChannelId.Value);
        }
        seed.Add(member);
        await seed.SaveChangesAsync();

        // Reload with the User navigation populated to hand to the command.
        await using var read = fixture.CreateDbContext();
        return (await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId))!;
    }

    [Fact]
    public async Task CreatePrivateChannel_PersistsTheCreatedChannelId()
    {
        var member = await SeedLinkedMemberAsync();

        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .CreatePrivateTextChannelAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<ulong>?>(), Arg.Any<IEnumerable<ulong>?>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)888UL);

        var result = await host.SendAsync(new CreateMemberPrivateChannelCommand(member));

        result.Should().Be(888UL);

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().Be(888UL);
    }

    [Fact]
    public async Task DeletePrivateChannel_ClearsTheChannelId_WhenDeletionSucceeds()
    {
        var member = await SeedLinkedMemberAsync(privateChannelId: 555UL);

        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .DeleteTextChannelAsync(555UL, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await host.SendAsync(new DeleteMemberPrivateChannelCommand(member));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(member.UserId);
        persisted!.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task DeletePrivateChannel_ReturnsNotFound_WhenNoChannelConfigured()
    {
        var member = await SeedLinkedMemberAsync();

        using var host = CreateHost();
        var result = await host.SendAsync(new DeleteMemberPrivateChannelCommand(member));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
