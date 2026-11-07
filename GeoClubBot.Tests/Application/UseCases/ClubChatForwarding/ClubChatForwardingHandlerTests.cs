using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubChatForwarding;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubChatForwarding;

public sealed class ClubChatForwardingHandlerTests
{
    private const ulong Admin = 10;
    private const ulong MessageId = 99;
    private static readonly Guid ClubA = Guid.NewGuid();
    private static readonly Guid ClubB = Guid.NewGuid();
    private static readonly Guid OptedOut = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly IDiscordMemberPermissionAccess _permissions = Substitute.For<IDiscordMemberPermissionAccess>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubChatForwardRepository _forwards = Substitute.For<IClubChatForwardRepository>();
    private readonly IGeoGuessrClubChatSender _sender = Substitute.For<IGeoGuessrClubChatSender>();

    private readonly ClubChatForwardingConfiguration _options = new()
    {
        Enabled = true,
        AdminRoleIds = [5],
        Template = "{text}\n\n– {author} in #{channel}",
    };

    public ClubChatForwardingHandlerTests()
    {
        _permissions.IsAdministratorOrHasAnyRoleAsync(Admin, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _clubs.ReadAllClubsAsync(Arg.Any<CancellationToken>())
            .Returns([global::Entities.Club.Create(ClubA, "Dragons", 10), global::Entities.Club.Create(ClubB, "Den", 5)]);
        _forwards.ReadByMessageAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns([]);
        _sender.SendAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => new ClubChatDelivery(ClubChatDeliveryStatus.Confirmed, call.Arg<IReadOnlyList<string>>().Count, call.Arg<IReadOnlyList<string>>().Count));
    }

    private ClubChatForwardingAccess Access() => new(
        _permissions, _clubs, Options.Create(_options),
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "x",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs =
            [
                new GeoGuessrClubEntry { ClubId = ClubA, NcfaToken = "a", IsMain = true },
                new GeoGuessrClubEntry { ClubId = ClubB, NcfaToken = "b" },
                new GeoGuessrClubEntry { ClubId = OptedOut, NcfaToken = "c", ForwardChat = false },
            ],
        }));

    private Task<Result<ClubChatForwardDraft>> PrepareAsync(ulong actor = Admin) =>
        new PrepareClubChatForwardHandler(Access(), _forwards, Options.Create(_options))
            .Handle(new PrepareClubChatForwardQuery(MessageId, actor, "Big news", "Fibs", "announcements"), CancellationToken.None);

    private Task<Result<ClubChatForwardReport>> ForwardAsync(string text, IReadOnlyList<Guid> clubs, ulong actor = Admin) =>
        new ForwardToClubChatHandler(Access(), _sender, _forwards, Options.Create(_options), new FakeTimeProvider(Now),
                NullLogger<ForwardToClubChatHandler>.Instance)
            .Handle(new ForwardToClubChatCommand(MessageId, actor, text, clubs), CancellationToken.None);

    [Fact]
    public async Task TheForm_IsPrefilledFromTheTemplate_WithTheClubsThatTakeForwards()
    {
        var draft = (await PrepareAsync()).Value;

        draft.Text.Should().Be("Big news\n\n– Fibs in #announcements");
        draft.PartCount.Should().Be(1);
        draft.Clubs.Should().Equal(new ClubChatTarget(ClubA, "Dragons"), new ClubChatTarget(ClubB, "Den"));
    }

    [Fact]
    public async Task TheForm_ListsEarlierForwards_ByClubName()
    {
        _forwards.ReadByMessageAsync(MessageId, Arg.Any<CancellationToken>())
            .Returns([ClubChatForward.Create(MessageId, ClubB, 7, Now.AddHours(-1), 2, 2, 1)]);

        var draft = (await PrepareAsync()).Value;

        draft.EarlierForwards.Should().Equal(new EarlierClubChatForward("Den", 7, Now.AddHours(-1), 2, 1));
    }

    [Fact]
    public async Task NonAdmins_AreRefused_AndTheConfiguredRolesAreChecked()
    {
        (await PrepareAsync(actor: 11)).Error.Type.Should().Be(ErrorType.Forbidden);
        (await ForwardAsync("Hi", [ClubA], actor: 11)).Error.Type.Should().Be(ErrorType.Forbidden);

        await _permissions.Received().IsAdministratorOrHasAnyRoleAsync(11,
            Arg.Is<IReadOnlyCollection<ulong>>(r => r.SequenceEqual(new ulong[] { 5 })), Arg.Any<CancellationToken>());
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Fact]
    public async Task WhenSwitchedOff_NothingIsSent()
    {
        _options.Enabled = false;

        (await ForwardAsync("Hi", [ClubA])).Error.Should().Be(ClubChatForwardingAccess.Disabled);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Fact]
    public async Task SendsTheSplitPartsToEachChosenClub_AndRecordsEachForward()
    {
        var text = new string('a', 250) + "\n\n" + new string('b', 250);

        var report = (await ForwardAsync(text, [ClubA, ClubB])).Value;

        report.PartCount.Should().Be(2);
        report.Clubs.Select(c => c.Club.Name).Should().Equal("Dragons", "Den");
        await _sender.Received(1).SendAsync(ClubA,
            Arg.Is<IReadOnlyList<string>>(p => p.Count == 2 && p[0].StartsWith("(1/2) ")), _options.EchoTimeout, Arg.Any<CancellationToken>());
        _forwards.Received(1).Add(Arg.Is<ClubChatForward>(f =>
            f.ClubId == ClubA && f.DiscordMessageId == MessageId && f.ForwardedByDiscordUserId == Admin
            && f.ForwardedAt == Now && f.PartCount == 2 && f.PartsConfirmed == 2));
        _forwards.Received(1).Add(Arg.Is<ClubChatForward>(f => f.ClubId == ClubB));
    }

    [Fact]
    public async Task AClubThatOptedOut_OrIsUnknown_IsNeverSentTo()
    {
        var result = await ForwardAsync("Hi", [OptedOut, Guid.NewGuid()]);

        result.Error.Code.Should().Be("club-chat.no_club");
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ATextTooLongToSend_IsRefusedBeforeAnythingIsSent()
    {
        var result = await ForwardAsync(string.Join(' ', Enumerable.Repeat("word", 600)), [ClubA]);

        result.Error.Code.Should().Be("club-chat.too_long");
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Fact]
    public async Task AClubThatCouldNotBeReached_IsReported_ButNotRecorded()
    {
        _sender.SendAsync(ClubB, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ClubChatDelivery(ClubChatDeliveryStatus.NotConnected, 0, 0, "could not connect"));

        var report = (await ForwardAsync("Hi", [ClubA, ClubB])).Value;

        report.Clubs.Single(c => c.Club.ClubId == ClubB).Delivery.Status.Should().Be(ClubChatDeliveryStatus.NotConnected);
        _forwards.Received(1).Add(Arg.Is<ClubChatForward>(f => f.ClubId == ClubA));
        _forwards.DidNotReceive().Add(Arg.Is<ClubChatForward>(f => f.ClubId == ClubB));
    }

    [Fact]
    public async Task AnUnconfirmedForward_IsRecorded_SoARepeatIsWarnedAbout()
    {
        _sender.SendAsync(ClubA, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ClubChatDelivery(ClubChatDeliveryStatus.Unconfirmed, 1, 0, "no echo"));

        await ForwardAsync("Hi", [ClubA]);

        _forwards.Received(1).Add(Arg.Is<ClubChatForward>(f => f.ClubId == ClubA && f.PartsSent == 1 && f.PartsConfirmed == 0));
    }

    [Fact]
    public async Task OneClubFailingUnexpectedly_DoesNotLoseTheOthersReport()
    {
        _sender.SendAsync(ClubA, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<ClubChatDelivery>(_ => throw new InvalidOperationException("boom"));

        var report = (await ForwardAsync("Hi", [ClubA, ClubB])).Value;

        report.Clubs.Select(c => c.Delivery.Status).Should().Equal(ClubChatDeliveryStatus.NotConnected, ClubChatDeliveryStatus.Confirmed);
    }
}
