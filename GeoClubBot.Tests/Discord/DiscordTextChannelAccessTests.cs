using System.Collections.Immutable;
using Discord;
using FluentAssertions;
using GeoClubBot.Discord.OutputAdapters;
using UseCases.OutputPorts.Discord;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class DiscordTextChannelAccessTests
{
    [Fact]
    public void AccessOf_AChannelWhosePayloadHadNoOverwrites_IsEmpty_InsteadOfThrowing()
    {
        // What Discord.Net leaves when Discord omits permission_overwrites (#483).
        var access = DiscordDiscordTextChannelAccess.AccessOf(default(ImmutableArray<Overwrite>));

        access.Should().BeEmpty();
    }

    [Fact]
    public void AccessOf_ReadsEachOverwrite()
    {
        var overwrites = ImmutableArray.Create(
            new Overwrite(7, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new Overwrite(42, PermissionTarget.User, new OverwritePermissions(
                viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)));

        var access = DiscordDiscordTextChannelAccess.AccessOf(overwrites);

        access.Should().Equal(
            new ChannelAccess(7, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.View),
            new ChannelAccess(42, ChannelAccessTarget.User,
                ChannelRights.View | ChannelRights.SendMessages | ChannelRights.ReadMessageHistory, ChannelRights.None));
    }
}
