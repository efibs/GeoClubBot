using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.SelfRoles;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the self-roles message use case through the real MediatR pipeline. The Discord ports
/// are substituted so the test asserts which Discord call the handler chooses (create / update)
/// based on whether a previous message exists.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SelfRolesUseCaseIntegrationTests(PostgresFixture fixture)
{
    private const ulong SelfRolesChannelId = 123456UL;

    private MediatorTestHost CreateHost() =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["SelfRoles:TextChannelId"] = SelfRolesChannelId.ToString(),
            ["SelfRoles:Roles:0:RoleId"] = "99",
            ["SelfRoles:Roles:0:RoleEmoji"] = "X",
            ["SelfRoles:Roles:0:RoleDescription"] = "A role",
        });

    [Fact]
    public async Task UpdateSelfRolesMessage_SendsANewMessage_WhenNonePreviouslyExists()
    {
        using var host = CreateHost();
        // No prior message: the substituted channel access returns null for the last-message lookup.

        await host.SendAsync(new UpdateSelfRolesMessageCommand());

        await host.Mock<IDiscordMessageAccess>()
            .Received(1)
            .SendSelfRolesMessageAsync(SelfRolesChannelId, Arg.Any<IEnumerable<SelfRoleSetting>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateSelfRolesMessage_UpdatesTheExistingMessage_WhenOneIsPresent()
    {
        using var host = CreateHost();
        host.Mock<IDiscordTextChannelAccess>()
            .ReadLastMessageOfUserAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)777UL);

        await host.SendAsync(new UpdateSelfRolesMessageCommand());

        await host.Mock<IDiscordMessageAccess>()
            .Received(1)
            .UpdateSelfRolesMessageAsync(SelfRolesChannelId, 777UL, Arg.Any<IEnumerable<SelfRoleSetting>>(), Arg.Any<CancellationToken>());
    }
}
