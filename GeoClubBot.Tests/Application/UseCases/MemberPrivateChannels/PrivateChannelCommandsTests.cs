using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using GeoClubBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.MemberPrivateChannels;

/// <summary>
/// The commands of a private channel's life — create, archive, restore, move, delete — against an
/// in-memory Discord server, so the categories they pick, create and empty can be checked.
/// </summary>
public sealed class PrivateChannelCommandsTests
{
    private const ulong ChannelId = 500;
    private const ulong DiscordUserId = 42;
    private const ulong ArchiveCategory = 20;
    private const ulong LegacyCategory = 30;
    private const ulong ClubRoleId = 70;
    private const ulong OtherClubRoleId = 71;
    private const ulong AdminRoleId = 80;
    private const string UserId = "user-1";

    private static readonly Guid ClubA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClubB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly TimeSpan KeepTime = TimeSpan.FromDays(30);

    private readonly FakeDiscordChannels _discord = new();
    private readonly FakeMemberPrivateChannelCategories _categories = new();
    private readonly IDiscordMessageAccess _messages = Substitute.For<IDiscordMessageAccess>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly MemberPrivateChannelsConfiguration _config = new()
    {
        Description = "Talk to the admins",
        MaxChannelsPerCategory = 2,
        AdminRoleIds = [AdminRoleId],
        ArchiveCategoryId = ArchiveCategory,
        ArchiveKeepTimeSpan = KeepTime,
        ArchiveCleanupSchedule = "0 0 0 * * ?"
    };

    public PrivateChannelCommandsTests()
    {
        _discord.AddCategory(ArchiveCategory, "archive");
        _discord.AddCategory(LegacyCategory, "private channels");
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>()).Returns(Entities.Club.Create(ClubA, "Alpha", 1));
        _clubs.ReadClubByIdAsync(ClubB, Arg.Any<CancellationToken>()).Returns(Entities.Club.Create(ClubB, "Beta", 1));
        _members.ReadMembersWithArchivedPrivateChannelsOldestFirstAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private MemberPrivateChannelManager Manager() =>
        new(_discord, _messages, _members, _clubs, _categories, _unitOfWork, Options.Create(_config),
            Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 * * * ?",
                ActivityNcfaToken = "t",
                UserProfileNcfaToken = "t",
                Clubs =
                [
                    new GeoGuessrClubEntry { ClubId = ClubA, NcfaToken = "a", IsMain = true, RoleId = ClubRoleId },
                    new GeoGuessrClubEntry { ClubId = ClubB, NcfaToken = "b", RoleId = OtherClubRoleId }
                ]
            }),
            NullLogger<MemberPrivateChannelManager>.Instance);

    private static ClubMemberBuilder Member(string userId = UserId, ulong discordUserId = DiscordUserId,
        string nickname = "Player1") =>
        new ClubMemberBuilder().WithUserId(userId).WithNickname(nickname).WithDiscordUserId(discordUserId).InClub(ClubA);

    private ClubMember Tracked(ClubMember member)
    {
        _members.ReadForUpdateByUserIdAsync(member.UserId, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    /// <summary>A category the bot already made for a club, holding <paramref name="channels"/> channels.</summary>
    private ulong ClubCategory(Guid clubId, int index, int channels = 0)
    {
        var id = (ulong)(100 + index + (clubId == ClubB ? 50 : 0));
        _discord.AddCategory(id, $"cat {index}");
        _categories.Add(MemberPrivateChannelCategory.Create(id, clubId, index));
        for (var i = 0; i < channels; i++)
        {
            _discord.AddChannel(id * 1000 + (ulong)i, id);
        }

        return id;
    }

    private ulong ParentOf(ulong channelId) => _discord.Channels[channelId].Parent!.Value;

    // ---- Create -----------------------------------------------------------

    private Task<ulong?> CreateAsync(ClubMember member) =>
        new CreateMemberPrivateChannelHandler(Manager(), NullLogger<CreateMemberPrivateChannelHandler>.Instance)
            .Handle(new CreateMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Create_OfTheFirstMember_OpensTheClubsCategory_VisibleToTheClubAndAdmins()
    {
        var tracked = Tracked(Member().Build());

        var channelId = await CreateAsync(Member().Build());

        var category = _discord.CategoriesNamed("Alpha – Private").Should().ContainSingle().Subject;
        category.Access.Should().BeEquivalentTo(new[]
        {
            new ChannelAccess(FakeDiscordChannels.EveryoneRoleId, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.View),
            new ChannelAccess(FakeDiscordChannels.BotUserId, ChannelAccessTarget.User,
                ChannelRights.View | ChannelRights.SendMessages | ChannelRights.ReadMessageHistory, ChannelRights.None),
            new ChannelAccess(ClubRoleId, ChannelAccessTarget.Role, ChannelRights.View, ChannelRights.None),
            new ChannelAccess(AdminRoleId, ChannelAccessTarget.Role, ChannelRights.View, ChannelRights.None)
        });
        _categories.Rows.Should().ContainSingle(r => r.CategoryId == category.Id && r.ClubId == ClubA && r.Index == 1);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());

        channelId.Should().NotBeNull();
        ParentOf(channelId!.Value).Should().Be(category.Id);
        tracked.PrivateTextChannelId.Should().Be(channelId);
    }

    [Fact]
    public async Task Create_MakesTheChannelVisibleOnlyToTheMemberAndTheAdmins()
    {
        var channelId = await CreateAsync(Member().Build());

        var channel = _discord.Channels[channelId!.Value];
        channel.Name.Should().Be("player1-private-channel");
        channel.Access.Should().BeEquivalentTo(new[]
        {
            new ChannelAccess(FakeDiscordChannels.EveryoneRoleId, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.View),
            new ChannelAccess(FakeDiscordChannels.BotUserId, ChannelAccessTarget.User,
                ChannelRights.View | ChannelRights.SendMessages | ChannelRights.ReadMessageHistory, ChannelRights.None),
            new ChannelAccess(DiscordUserId, ChannelAccessTarget.User, ChannelRights.View, ChannelRights.None),
            new ChannelAccess(AdminRoleId, ChannelAccessTarget.Role, ChannelRights.View, ChannelRights.None)
        });
    }

    [Fact]
    public async Task Create_UsesTheClubsExistingCategory_WhileItHasRoom()
    {
        var first = ClubCategory(ClubA, 1, channels: 1);

        var channelId = await CreateAsync(Member().Build());

        ParentOf(channelId!.Value).Should().Be(first);
        _discord.Writes.Should().NotContain(w => w.StartsWith("create-category"));
    }

    [Fact]
    public async Task Create_WhenTheClubsCategoryIsFull_OpensANumberedOne()
    {
        ClubCategory(ClubA, 1, channels: 2);

        var channelId = await CreateAsync(Member().Build());

        var second = _discord.CategoriesNamed("Alpha – Private 2").Should().ContainSingle().Subject;
        ParentOf(channelId!.Value).Should().Be(second.Id);
        _categories.Rows.Should().Contain(r => r.CategoryId == second.Id && r.Index == 2);
    }

    [Fact]
    public async Task Create_NeverPutsAMemberIntoTheOtherClubsCategory()
    {
        ClubCategory(ClubB, 1);

        var channelId = await CreateAsync(Member().Build());

        _discord.CategoriesNamed("Alpha – Private").Should().ContainSingle()
            .Which.Id.Should().Be(ParentOf(channelId!.Value));
    }

    [Fact]
    public async Task Create_AfterAnAdminDeletedTheCategory_ForgetsItAndReusesItsNumber()
    {
        _categories.Add(MemberPrivateChannelCategory.Create(999, ClubA, 1));

        await CreateAsync(Member().Build());

        _categories.Rows.Should().ContainSingle(r => r.ClubId == ClubA)
            .Which.Should().Match<MemberPrivateChannelCategory>(r => r.CategoryId != 999 && r.Index == 1);
    }

    [Fact]
    public async Task Create_WelcomesTheMemberInTheirChannel()
    {
        var channelId = await CreateAsync(Member().Build());

        await _messages.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.StartsWith($"Welcome <@{DiscordUserId}>! This is your private space")),
            channelId!.Value,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ThatDiscordRefuses_RemembersNothing()
    {
        _discord.RefuseCreates = true;
        var tracked = Tracked(Member().Build());

        var result = await CreateAsync(Member().Build());

        result.Should().BeNull();
        tracked.PrivateTextChannelId.Should().BeNull();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ForAMemberInNoClub_DoesNothing()
    {
        var result = await CreateAsync(Member().InClub(null).Build());

        result.Should().BeNull();
        _discord.Writes.Should().BeEmpty();
    }

    // ---- Archive ----------------------------------------------------------

    private Task<Result> ArchiveAsync(ClubMember? member) =>
        new ArchiveMemberPrivateChannelHandler(Manager(), NullLogger<ArchiveMemberPrivateChannelHandler>.Instance)
            .Handle(new ArchiveMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Archive_WithoutAChannel_IsNotFound()
    {
        (await ArchiveAsync(null)).Error.Code.Should().Be("member_private_channel.not_found");
        (await ArchiveAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");

        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Archive_OfAnArchivedChannel_KeepsItsRetentionClock()
    {
        _discord.AddChannel(ChannelId, ArchiveCategory);
        var archivedAt = DateTimeOffset.UtcNow.AddDays(-3);

        var result = await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId, archivedAt).Build());

        result.IsSuccess.Should().BeTrue();
        _discord.Writes.Should().BeEmpty();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Archive_MovesTheChannelOnly_AndStartsTheRetentionClock()
    {
        var category = ClubCategory(ClubA, 1);
        _discord.AddChannel(ChannelId, category, "player1-private-channel");
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId).Build());

        result.IsSuccess.Should().BeTrue();
        ParentOf(ChannelId).Should().Be(ArchiveCategory);
        _discord.Channels[ChannelId].Name.Should().Be("player1-private-channel");
        tracked.PrivateTextChannelArchivedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Archive_SaysWhenTheChannelWillGo_WithoutPingingAnyone()
    {
        _discord.AddChannel(ChannelId, LegacyCategory);
        string? notice = null;
        _messages
            .When(m => m.SendMessageAsync(Arg.Any<string>(), ChannelId, Arg.Any<CancellationToken>()))
            .Do(call => notice = call.Arg<string>());

        await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId).Build());

        var deletion = DateTimeOffset.UtcNow.Add(KeepTime);
        notice.Should().StartWith("Player1 is no longer in one of our clubs")
            .And.Contain($"deleted on {deletion:yyyy-MM-dd}.")
            .And.NotContain("<@");
    }

    [Fact]
    public async Task Archive_ThatDiscordRefuses_LeavesTheMemberAlone()
    {
        _discord.AddChannel(ChannelId, LegacyCategory);
        _discord.RefuseUpdates = true;
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId).Build());

        result.Error.Code.Should().Be("member_private_channel.archive_failed");
        tracked.PrivateTextChannelArchivedAt.Should().BeNull();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Archive_IntoAFullArchive_DeletesTheChannelArchivedTheLongest()
    {
        _discord.AddChannel(ChannelId, LegacyCategory);
        _discord.AddChannel(601, ArchiveCategory);
        _discord.AddChannel(602, ArchiveCategory);
        var oldest = Tracked(Member("old", 1).InClub(null).WithPrivateChannel(601, DateTimeOffset.UtcNow.AddDays(-20)).Build());
        var newer = Tracked(Member("new", 2).InClub(null).WithPrivateChannel(602, DateTimeOffset.UtcNow.AddDays(-2)).Build());
        _members.ReadMembersWithArchivedPrivateChannelsOldestFirstAsync(Arg.Any<CancellationToken>()).Returns([oldest, newer]);

        var result = await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId).Build());

        result.IsSuccess.Should().BeTrue();
        _discord.Channels.Should().NotContainKey(601UL).And.ContainKey(602UL);
        ParentOf(ChannelId).Should().Be(ArchiveCategory);
        oldest.PrivateTextChannelId.Should().BeNull();
        newer.PrivateTextChannelId.Should().Be(602);
    }

    [Fact]
    public async Task Archive_IntoAnArchiveFullOfChannelsTheBotDoesNotOwn_FailsWithoutDeletingAnything()
    {
        _discord.AddChannel(ChannelId, LegacyCategory);
        _discord.AddChannel(601, ArchiveCategory);
        _discord.AddChannel(602, ArchiveCategory);

        var result = await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId).Build());

        result.Error.Code.Should().Be("member_private_channel.archive_full");
        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Archive_OfTheLastChannelInAnOverflowCategory_DeletesThatCategory()
    {
        var first = ClubCategory(ClubA, 1);
        var second = ClubCategory(ClubA, 2);
        _discord.AddChannel(ChannelId, second);

        await ArchiveAsync(Member().InClub(null).WithPrivateChannel(ChannelId).Build());

        _discord.Channels.Should().NotContainKey(second).And.ContainKey(first);
        _categories.Rows.Should().ContainSingle().Which.CategoryId.Should().Be(first);
    }

    // ---- Restore ----------------------------------------------------------

    private Task<Result> RestoreAsync(ClubMember? member) =>
        new RestoreMemberPrivateChannelHandler(Manager(), NullLogger<RestoreMemberPrivateChannelHandler>.Instance)
            .Handle(new RestoreMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Restore_WithoutAChannel_IsNotFound()
    {
        (await RestoreAsync(null)).Error.Code.Should().Be("member_private_channel.not_found");
        (await RestoreAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");

        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_MovesTheChannelIntoTheClubsCategory_RenamesIt_AndClearsTheRetentionClock()
    {
        var category = ClubCategory(ClubA, 1);
        _discord.AddChannel(ChannelId, ArchiveCategory, "oldname-private-channel");
        var archivedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        var result = await RestoreAsync(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        result.IsSuccess.Should().BeTrue();
        ParentOf(ChannelId).Should().Be(category);
        _discord.Channels[ChannelId].Name.Should().Be("player1-private-channel");
        tracked.PrivateTextChannelArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Restore_ReappliesThePermissions()
    {
        _discord.AddChannel(ChannelId, ArchiveCategory, access:
            [new ChannelAccess(DiscordUserId, ChannelAccessTarget.User, ChannelRights.View, ChannelRights.None)]);

        await RestoreAsync(Member().WithPrivateChannel(ChannelId, DateTimeOffset.UtcNow).Build());

        _discord.Channels[ChannelId].Access.Should().Contain(
            new ChannelAccess(FakeDiscordChannels.EveryoneRoleId, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.View));
    }

    [Fact]
    public async Task Restore_WelcomesALinkedMemberBack()
    {
        _discord.AddChannel(ChannelId, ArchiveCategory);

        await RestoreAsync(Member().WithPrivateChannel(ChannelId, DateTimeOffset.UtcNow).Build());

        await _messages.Received(1).SendMessageAsync(
            $"Welcome back <@{DiscordUserId}>! Your private channel is active again.",
            ChannelId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_OfAnUnlinkedMember_PingsNobody()
    {
        _discord.AddChannel(ChannelId, ArchiveCategory);
        var unlinked = new ClubMemberBuilder().WithUserId(UserId).InClub(ClubA)
            .WithPrivateChannel(ChannelId, DateTimeOffset.UtcNow).Build();

        var result = await RestoreAsync(unlinked);

        result.IsSuccess.Should().BeTrue();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_ThatDiscordRefuses_KeepsTheChannelArchived()
    {
        _discord.AddChannel(ChannelId, ArchiveCategory);
        ClubCategory(ClubA, 1);
        _discord.RefuseUpdates = true;
        var archivedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        var result = await RestoreAsync(Member().WithPrivateChannel(ChannelId, archivedAt).Build());

        result.Error.Code.Should().Be("member_private_channel.restore_failed");
        tracked.PrivateTextChannelArchivedAt.Should().Be(archivedAt);
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Move to club (club switch) ----------------------------------------

    private Task<Result<bool>> MoveAsync(ClubMember? member) =>
        new MoveMemberPrivateChannelToClubHandler(Manager(), NullLogger<MoveMemberPrivateChannelToClubHandler>.Instance)
            .Handle(new MoveMemberPrivateChannelToClubCommand(member), CancellationToken.None);

    [Fact]
    public async Task Move_AfterASwitch_TakesTheChannelIntoTheNewClubsCategory_AndDropsTheEmptiedOverflow()
    {
        ClubCategory(ClubA, 1);
        var oldOverflow = ClubCategory(ClubA, 2);
        var newCategory = ClubCategory(ClubB, 1);
        _discord.AddChannel(ChannelId, oldOverflow);

        var result = await MoveAsync(Member().InClub(ClubB).WithPrivateChannel(ChannelId).Build());

        result.Value.Should().BeTrue();
        ParentOf(ChannelId).Should().Be(newCategory);
        _discord.Channels.Should().NotContainKey(oldOverflow);
    }

    [Fact]
    public async Task Move_OfAChannelAlreadyInItsClubsCategory_DoesNothing()
    {
        var category = ClubCategory(ClubA, 1);
        _discord.AddChannel(ChannelId, category);

        var result = await MoveAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.Value.Should().BeFalse();
        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_WithoutAChannel_IsNotFound()
    {
        (await MoveAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");
    }

    // ---- Delete -----------------------------------------------------------

    private Task<Result> DeleteAsync(ClubMember? member) =>
        new DeleteMemberPrivateChannelHandler(_discord, _members, NullLogger<DeleteMemberPrivateChannelHandler>.Instance)
            .Handle(new DeleteMemberPrivateChannelCommand(member), CancellationToken.None);

    [Fact]
    public async Task Delete_WithoutAChannel_IsNotFound()
    {
        (await DeleteAsync(null)).Error.Code.Should().Be("member_private_channel.not_found");
        (await DeleteAsync(Member().Build())).Error.Code.Should().Be("member_private_channel.not_found");

        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_RemovesTheChannelAndForgetsIt()
    {
        _discord.AddChannel(ChannelId, LegacyCategory);
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await DeleteAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.IsSuccess.Should().BeTrue();
        _discord.Channels.Should().NotContainKey(ChannelId);
        tracked.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ThatDiscordRefuses_IsReported_ButForgotten_AsTheChannelMayBeGoneAlready()
    {
        var tracked = Tracked(Member().WithPrivateChannel(ChannelId).Build());

        var result = await DeleteAsync(Member().WithPrivateChannel(ChannelId).Build());

        result.Error.Code.Should().Be("member_private_channel.delete_failed");
        tracked.PrivateTextChannelId.Should().BeNull();
    }
}
