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
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.MemberPrivateChannels;

/// <summary>
/// The reconcile that migrates the old shared category, recovers missing channels and fixes
/// permissions: its pure planner, the access policy it compares against, and a whole run.
/// </summary>
public sealed class PrivateChannelReconcileTests
{
    private const ulong ArchiveCategory = 20;
    private const ulong LegacyCategory = 30;
    private const ulong AdminRoleId = 80;

    private static readonly Guid ClubA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClubB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeDiscordChannels _discord = new();
    private readonly FakeMemberPrivateChannelCategories _categories = new();
    private readonly IDiscordMessageAccess _messages = Substitute.For<IDiscordMessageAccess>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly List<ClubMember> _stored = [];

    private readonly MemberPrivateChannelsConfiguration _config = new()
    {
        Description = "Talk to the admins",
        MaxChannelsPerCategory = 2,
        AdminRoleIds = [AdminRoleId],
        ArchiveCategoryId = ArchiveCategory,
        ArchiveKeepTimeSpan = TimeSpan.FromDays(30),
        ArchiveCleanupSchedule = "0 0 0 * * ?"
    };

    public PrivateChannelReconcileTests()
    {
        _discord.AddCategory(ArchiveCategory, "archive");
        _discord.AddCategory(LegacyCategory, "private channels");

        var alpha = Entities.Club.Create(ClubA, "Alpha", 1);
        var beta = Entities.Club.Create(ClubB, "Beta", 1);
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>()).Returns(alpha);
        _clubs.ReadClubByIdAsync(ClubB, Arg.Any<CancellationToken>()).Returns(beta);
        _clubs.ReadAllClubsAsync(Arg.Any<CancellationToken>()).Returns(new List<Entities.Club> { alpha, beta });

        _members.ReadClubMembersAsync(Arg.Any<CancellationToken>()).Returns(_ => _stored.ToList());
        _members.ReadMembersWithArchivedPrivateChannelsOldestFirstAsync(Arg.Any<CancellationToken>())
            .Returns(_ => _stored
                .Where(m => m.ClubId is null && m.PrivateTextChannelArchivedAt is not null)
                .OrderBy(m => m.PrivateTextChannelArchivedAt)
                .ToList());
        _members.ReadForUpdateByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.SingleOrDefault(m => m.UserId == call.Arg<string>()));
    }

    private ClubMember Store(string nickname, Guid? clubId, ulong discordUserId, ulong? channelId = null,
        DateTimeOffset? archivedAt = null)
    {
        var builder = new ClubMemberBuilder()
            .WithUserId($"id-{nickname}").WithNickname(nickname).WithDiscordUserId(discordUserId).InClub(clubId);
        if (channelId is { } id)
        {
            builder.WithPrivateChannel(id, archivedAt);
        }

        var member = builder.Build();
        _stored.Add(member);
        return member;
    }

    private IReadOnlyList<ChannelAccess> RightAccess(ulong discordUserId) =>
        PrivateChannelAccessPolicy.ForChannel(
            FakeDiscordChannels.EveryoneRoleId, FakeDiscordChannels.BotUserId, discordUserId, [AdminRoleId]);

    private ReconcileMemberPrivateChannelsHandler Handler()
    {
        var geoGuessr = Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 * * * ?",
            ActivityNcfaToken = "t",
            UserProfileNcfaToken = "t",
            Clubs =
            [
                new GeoGuessrClubEntry { ClubId = ClubA, NcfaToken = "a", IsMain = true, RoleId = 70 },
                new GeoGuessrClubEntry { ClubId = ClubB, NcfaToken = "b", RoleId = 71 }
            ]
        });
        var manager = new MemberPrivateChannelManager(_discord, _messages, _members, _clubs, _categories, _unitOfWork,
            Options.Create(_config), geoGuessr, NullLogger<MemberPrivateChannelManager>.Instance);

        return new ReconcileMemberPrivateChannelsHandler(manager, _members, _clubs, _unitOfWork, Options.Create(_config),
            NullLogger<ReconcileMemberPrivateChannelsHandler>.Instance);
    }

    private async Task<PrivateChannelReconcileReport> ReconcileAsync(bool dryRun) =>
        (await Handler().Handle(new ReconcileMemberPrivateChannelsCommand(dryRun), CancellationToken.None)).Value;

    private ulong ParentOf(ulong channelId) => _discord.Channels[channelId].Parent!.Value;

    // ---- A whole run ---------------------------------------------------------

    /// <summary>
    /// The production starting point: one shared category holding everyone's channel, one member whose
    /// channel creation failed when that category was full.
    /// </summary>
    private void TheOldLayout()
    {
        for (var i = 1; i <= 3; i++)
        {
            var channel = (ulong)(500 + i);
            _discord.AddChannel(channel, LegacyCategory, access:
            [
                new ChannelAccess(FakeDiscordChannels.EveryoneRoleId, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.View),
                new ChannelAccess((ulong)i, ChannelAccessTarget.User, ChannelRights.View, ChannelRights.None)
            ]);
            Store($"alpha{i}", ClubA, (ulong)i, channel);
        }

        _discord.AddChannel(510, LegacyCategory);
        Store("beta1", ClubB, 10, 510);
        Store("lost", ClubB, 11);
    }

    [Fact]
    public async Task DryRun_ReportsTheMigration_WithoutChangingAnything()
    {
        TheOldLayout();

        var report = await ReconcileAsync(dryRun: true);

        report.DryRun.Should().BeTrue();
        report.Repairs[PrivateChannelRepair.MoveToClub].Should().BeEquivalentTo("alpha1", "alpha2", "alpha3", "beta1");
        report.Repairs[PrivateChannelRepair.Create].Should().BeEquivalentTo("lost");
        report.AccessFixed.Should().HaveCount(4);
        report.CategoriesToCreate.Should().BeEquivalentTo(new Dictionary<string, int> { ["Alpha"] = 2, ["Beta"] = 1 });
        _discord.Writes.Should().BeEmpty();
        _categories.Rows.Should().BeEmpty();
        _stored.Single(m => m.User.Nickname == "lost").PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task Run_MovesEveryChannelIntoItsClubsCategories_AndRecoversTheLostOne()
    {
        TheOldLayout();

        var report = await ReconcileAsync(dryRun: false);

        report.Failures.Should().BeEmpty();
        var alpha1 = _discord.CategoriesNamed("Alpha – Private").Should().ContainSingle().Subject;
        var alpha2 = _discord.CategoriesNamed("Alpha – Private 2").Should().ContainSingle().Subject;
        var beta = _discord.CategoriesNamed("Beta – Private").Should().ContainSingle().Subject;

        new[] { ParentOf(501), ParentOf(502), ParentOf(503) }.Should().BeEquivalentTo(new[] { alpha1.Id, alpha1.Id, alpha2.Id });
        ParentOf(510).Should().Be(beta.Id);

        var lost = _stored.Single(m => m.User.Nickname == "lost");
        lost.PrivateTextChannelId.Should().NotBeNull("the channel whose creation failed is created now");
        ParentOf(lost.PrivateTextChannelId!.Value).Should().Be(beta.Id);
        await _messages.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.StartsWith("Welcome <@11>!")), lost.PrivateTextChannelId.Value, Arg.Any<CancellationToken>());

        _discord.Channels[501].Access.Should().BeEquivalentTo(RightAccess(1));
        report.EmptiedCategoryIds.Should().Equal(LegacyCategory);
        report.CategoriesToCreate.Should().BeEquivalentTo(new Dictionary<string, int> { ["Alpha"] = 2, ["Beta"] = 1 });
    }

    [Fact]
    public async Task Run_IsIdempotent()
    {
        TheOldLayout();
        await ReconcileAsync(dryRun: false);
        _discord.Writes.Clear();

        var again = await ReconcileAsync(dryRun: false);

        again.NothingToDo.Should().BeTrue();
        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_ArchivesTheChannelOfAMemberInNoClub_AndForgetsADeletedOne()
    {
        _discord.AddChannel(520, LegacyCategory, access: RightAccess(20));
        var gone = Store("gone", null, 20, 520);
        var vanished = Store("vanished", null, 21, 521, DateTimeOffset.UtcNow.AddDays(-1));

        var report = await ReconcileAsync(dryRun: false);

        report.Repairs[PrivateChannelRepair.Archive].Should().Equal("gone");
        report.Repairs[PrivateChannelRepair.Forget].Should().Equal("vanished");
        ParentOf(520).Should().Be(ArchiveCategory);
        gone.PrivateTextChannelArchivedAt.Should().NotBeNull();
        vanished.PrivateTextChannelId.Should().BeNull();
    }

    [Fact]
    public async Task Run_RestoresAnArchivedChannelOfAMemberBackInAClub()
    {
        _discord.AddChannel(530, ArchiveCategory, access: RightAccess(30));
        var back = Store("back", ClubA, 30, 530, DateTimeOffset.UtcNow.AddDays(-1));

        var report = await ReconcileAsync(dryRun: false);

        report.Repairs[PrivateChannelRepair.Restore].Should().Equal("back");
        ParentOf(530).Should().Be(_discord.CategoriesNamed("Alpha – Private").Single().Id);
        back.PrivateTextChannelArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Run_OneFailure_DoesNotStopTheOthers()
    {
        _discord.AddChannel(540, LegacyCategory, access: RightAccess(40));
        Store("stuck", ClubA, 40, 540);
        Store("fresh", ClubA, 41);
        _discord.RefuseUpdates = true;

        var report = await ReconcileAsync(dryRun: false);

        report.Failures.Should().ContainSingle().Which.Should().StartWith("stuck:");
        report.Repairs[PrivateChannelRepair.Create].Should().Equal("fresh");
    }

    [Fact]
    public async Task Run_SkipsMembersWithoutALinkedDiscordAccount()
    {
        var unlinked = new ClubMemberBuilder().WithUserId("u").WithNickname("unlinked").InClub(ClubA).Build();
        _stored.Add(unlinked);

        var report = await ReconcileAsync(dryRun: false);

        report.NothingToDo.Should().BeTrue();
        _discord.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_FixesTheClubRoleOfACategoryThatDrifted()
    {
        _discord.AddCategory(100, "Alpha – Private");
        _categories.Add(MemberPrivateChannelCategory.Create(100, ClubA, 1));

        var report = await ReconcileAsync(dryRun: false);

        report.CategoryAccessFixed.Should().Be(1);
        _discord.Channels[100].Access.Should().Contain(
            new ChannelAccess(70, ChannelAccessTarget.Role, ChannelRights.View, ChannelRights.None));
    }

    // ---- Access policy ---------------------------------------------------------

    [Fact]
    public void Policy_IgnoresOrder_AndOverwritesWithoutManagedRights()
    {
        var wanted = RightAccess(5);
        var actual = wanted.Reverse()
            .Append(new ChannelAccess(999, ChannelAccessTarget.Role, ChannelRights.None, ChannelRights.None))
            .ToList();

        PrivateChannelAccessPolicy.Matches(actual, wanted).Should().BeTrue();
    }

    [Fact]
    public void Policy_SeesAStrangerWhoCanViewTheChannel()
    {
        var wanted = RightAccess(5);
        var actual = wanted
            .Append(new ChannelAccess(6, ChannelAccessTarget.User, ChannelRights.View, ChannelRights.None))
            .ToList();

        PrivateChannelAccessPolicy.Matches(actual, wanted).Should().BeFalse();
    }

    [Fact]
    public void Policy_NeverGrantsEveryoneAViewThroughTheAdminRoles()
    {
        var access = PrivateChannelAccessPolicy.ForChannel(
            FakeDiscordChannels.EveryoneRoleId, FakeDiscordChannels.BotUserId, 5,
            [FakeDiscordChannels.EveryoneRoleId, AdminRoleId, AdminRoleId]);

        access.Should().ContainSingle(a => a.TargetId == FakeDiscordChannels.EveryoneRoleId)
            .Which.Deny.Should().Be(ChannelRights.View);
        access.Count(a => a.TargetId == AdminRoleId).Should().Be(1);
    }

    // ---- Category naming ---------------------------------------------------------

    [Theory]
    [InlineData(1, "Alpha – Private")]
    [InlineData(2, "Alpha – Private 2")]
    [InlineData(3, "Alpha – Private 3")]
    public void CategoryName_NumbersTheOverflow(int index, string expected) =>
        _config.CategoryName("Alpha", index).Should().Be(expected);
}
