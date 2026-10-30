using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.MemberPrivateChannels;

/// <summary>The commands of a private channel's life: create, archive, restore, delete.</summary>
public sealed class PrivateChannelCommandsTests
{
    private const ulong ChannelId = 500;
    private const ulong DiscordUserId = 42;
    private const ulong LiveCategory = 10;
    private const ulong ArchiveCategory = 20;
    private const string UserId = "user-1";

    private static readonly TimeSpan KeepTime = TimeSpan.FromDays(30);

    private readonly IDiscordTextChannelAccess _channels = Substitute.For<IDiscordTextChannelAccess>();
    private readonly IDiscordMessageAccess _messages = Substitute.For<IDiscordMessageAccess>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();

    private readonly IOptions<MemberPrivateChannelsConfiguration> _options = Options.Create(new MemberPrivateChannelsConfiguration
    {
        CategoryId = LiveCategory,
        Description = "Talk to the admins",
        ArchiveCategoryId = ArchiveCategory,
        ArchiveKeepTimeSpan = KeepTime,
        ArchiveCleanupSchedule = "0 0 0 * * ?"
    });

    private TextChannel? _updated;

    public PrivateChannelCommandsTests()
    {
        _channels.UpdateTextChannelAsync(Arg.Any<TextChannel>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _updated = call.Arg<TextChannel>();
                return true;
            });
    }

    private static ClubMemberBuilder Member() =>
        new ClubMemberBuilder().WithUserId(UserId).WithNickname("Player1").WithDiscordUserId(DiscordUserId);

    private ClubMember Tracked(ClubMember member)
    {
        _members.ReadForUpdateByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    private void DiscordRefusesTheMove() =>
        _channels.UpdateTextChannelAsync(Arg.Any<TextChannel>(), Arg.Any<CancellationToken>()).Returns(false);

    // ---- Create -----------------------------------------------------------

    private CreateMemberPrivateChannelHandler CreateHandler() =>
        new(_members, _channels, _messages, _options, NullLogger<CreateMemberPrivateChannelHandler>.Instance);

    [Fact]
    public async Task Create_OpensTheChannelForTheMember_AndRemembersIt()
    {
        _channels.CreatePrivateTextChannelAsync(default, default!, default!, default, default, default)
            .ReturnsForAnyArgs(ChannelId);
        var tracked = Tracked(Member().Build());

        var result = await CreateHandler().Handle(new CreateMemberPrivateChannelCommand(Member().Build()), CancellationToken.None);

        result.Should().Be(ChannelId);
        tracked.PrivateTextChannelId.Should().Be(ChannelId);
        await _channels.Received(1).CreatePrivateTextChannelAsync(
            LiveCategory,
            "player1-private-channel",
            "Talk to the admins",
            Arg.Is<IEnumerable<ulong>>(ids => ids.SequenceEqual(new[] { DiscordUserId })),
            Arg.Is<IEnumerable<ulong>?>(ids => ids == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WelcomesTheMemberInTheirChannel()
    {
        _channels.CreatePrivateTextChannelAsync(default, default!, default!, default, default, default)
            .ReturnsForAnyArgs(ChannelId);

        await CreateHandler().Handle(new CreateMemberPrivateChannelCommand(Member().Build()), CancellationToken.None);

        await _messages.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.StartsWith($"Welcome <@{DiscordUserId}>! This is your private space")),
            ChannelId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ThatDiscordRefuses_RemembersNothing()
    {
        _channels.CreatePrivateTextChannelAsync(default, default!, default!, default, default, default)
            .ReturnsForAnyArgs((ulong?)null);
        var tracked = Tracked(Member().Build());

        var result = await CreateHandler().Handle(new CreateMemberPrivateChannelCommand(Member().Build()), CancellationToken.None);

        result.Should().BeNull();
        tracked.PrivateTextChannelId.Should().BeNull();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Archive ----------------------------------------------------------

    private Task<Result> ArchiveAsync(ClubMember? member) =>
        new ArchiveMemberPrivateChannelHandler(
                _channels, _messages, _members, _options, NullLogger<ArchiveMemberPrivateChannelHandler>.Instance)
            .Handle(new ArchiveMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Archive_WithoutAChannel_IsNotFound()
    {
        (await ArchiveAsync(null)).Error.Code.Should().Be("member_private_channel.not_found");
        (await ArchiveAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");

        _channels.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Archive_OfAnArchivedChannel_KeepsItsRetentionClock()
    {
        var archivedAt = DateTimeOffset.UtcNow.AddDays(-3);

        var result = await ArchiveAsync(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        result.IsSuccess.Should().BeTrue();
        _channels.ReceivedCalls().Should().BeEmpty();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Archive_MovesTheChannelOnly_AndStartsTheRetentionClock()
    {
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await ArchiveAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.IsSuccess.Should().BeTrue();
        _updated.Should().Be(new TextChannel(ChannelId) { CategoryId = ArchiveCategory });
        tracked.PrivateTextChannelArchivedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Archive_SaysWhenTheChannelWillGo_WithoutPingingAnyone()
    {
        string? notice = null;
        _messages
            .When(m => m.SendMessageAsync(Arg.Any<string>(), ChannelId, Arg.Any<CancellationToken>()))
            .Do(call => notice = call.Arg<string>());

        await ArchiveAsync(Member().WithPrivateChannel(ChannelId).Build());

        var deletion = DateTimeOffset.UtcNow.Add(KeepTime);
        notice.Should().StartWith("Player1 is no longer in one of our clubs")
            .And.Contain($"deleted on {deletion:yyyy-MM-dd}.")
            .And.NotContain("<@");
    }

    [Fact]
    public async Task Archive_ThatDiscordRefuses_LeavesTheMemberAlone()
    {
        DiscordRefusesTheMove();
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await ArchiveAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.Error.Code.Should().Be("member_private_channel.archive_failed");
        tracked.PrivateTextChannelArchivedAt.Should().BeNull();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Restore ----------------------------------------------------------

    private Task<Result> RestoreAsync(ClubMember? member) =>
        new RestoreMemberPrivateChannelHandler(
                _channels, _messages, _members, _options, NullLogger<RestoreMemberPrivateChannelHandler>.Instance)
            .Handle(new RestoreMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Restore_WithoutAChannel_IsNotFound()
    {
        (await RestoreAsync(null)).Error.Code.Should().Be("member_private_channel.not_found");
        (await RestoreAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");

        _channels.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_MovesTheChannelBack_RenamesIt_AndClearsTheRetentionClock()
    {
        var archivedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        var result = await RestoreAsync(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        result.IsSuccess.Should().BeTrue();
        _updated.Should().Be(new TextChannel(ChannelId) { Name = "player1-private-channel", CategoryId = LiveCategory });
        tracked.PrivateTextChannelArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Restore_WelcomesALinkedMemberBack()
    {
        await RestoreAsync(Member().WithPrivateChannel(ChannelId, DateTimeOffset.UtcNow).Build());

        await _messages.Received(1).SendMessageAsync(
            $"Welcome back <@{DiscordUserId}>! Your private channel is active again.",
            ChannelId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_OfAnUnlinkedMember_PingsNobody()
    {
        var unlinked = new ClubMemberBuilder().WithUserId(UserId).WithPrivateChannel(ChannelId, DateTimeOffset.UtcNow).Build();

        var result = await RestoreAsync(unlinked);

        result.IsSuccess.Should().BeTrue();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_ThatDiscordRefuses_KeepsTheChannelArchived()
    {
        DiscordRefusesTheMove();
        var archivedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        var result = await RestoreAsync(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        result.Error.Code.Should().Be("member_private_channel.restore_failed");
        tracked.PrivateTextChannelArchivedAt.Should().Be(archivedAt);
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Delete -----------------------------------------------------------

    private Task<Result> DeleteAsync(ClubMember? member) =>
        new DeleteMemberPrivateChannelHandler(_channels, _members, NullLogger<DeleteMemberPrivateChannelHandler>.Instance)
            .Handle(new DeleteMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Delete_WithoutAChannel_IsNotFound()
    {
        (await DeleteAsync(null)).Error.Code.Should().Be("member_private_channel.not_found");
        (await DeleteAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");

        _channels.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_RemovesTheChannelAndForgetsIt()
    {
        _channels.DeleteTextChannelAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(true);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await DeleteAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.IsSuccess.Should().BeTrue();
        tracked.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ThatDiscordRefuses_IsReported_ButForgotten_AsTheChannelMayBeGoneAlready()
    {
        _channels.DeleteTextChannelAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(false);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await DeleteAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.Error.Code.Should().Be("member_private_channel.delete_failed");
        tracked.PrivateTextChannelId.Should().BeNull();
    }
}
