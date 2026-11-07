using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubChatForwarding;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// A forward goes through the real MediatR pipeline: the command's unit of work stores the forward,
/// and the next form for the same message warns about it.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubChatForwardingUseCaseIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AForward_IsStored_AndTheNextFormForTheSameMessageWarnsAboutIt()
    {
        var clubId = Guid.NewGuid();
        var admin = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var messageId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        using var host = CreateHost(clubId);
        host.Mock<IDiscordMemberPermissionAccess>()
            .IsAdministratorOrHasAnyRoleAsync(admin, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        host.Mock<IGeoGuessrClubChatSender>()
            .SendAsync(clubId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ClubChatDelivery(ClubChatDeliveryStatus.Confirmed, 1, 1));

        var before = await host.SendAsync(new PrepareClubChatForwardQuery(messageId, admin, "News", "Fibs", "announcements"));
        before.Value.EarlierForwards.Should().BeEmpty();

        var report = await host.SendAsync(new ForwardToClubChatCommand(messageId, admin, before.Value.Text, [clubId]));
        report.Value.Clubs.Should().ContainSingle().Which.Delivery.Status.Should().Be(ClubChatDeliveryStatus.Confirmed);

        var after = await host.SendAsync(new PrepareClubChatForwardQuery(messageId, admin, "News", "Fibs", "announcements"));
        after.Value.EarlierForwards.Should().ContainSingle().Which.ForwardedByDiscordUserId.Should().Be(admin);
    }

    private MediatorTestHost CreateHost(Guid clubId) =>
        new(fixture.ConnectionString, services =>
        {
            services.RemoveAll<IOptions<ClubChatForwardingConfiguration>>();
            services.AddSingleton(Options.Create(new ClubChatForwardingConfiguration { Enabled = true }));
            services.RemoveAll<IOptions<GeoGuessrConfiguration>>();
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "x",
                ActivityNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = clubId, NcfaToken = "t", IsMain = true }],
            }));
        });
}
