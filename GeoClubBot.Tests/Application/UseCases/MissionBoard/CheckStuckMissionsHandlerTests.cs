using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MissionBoard;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.MissionBoard;

public sealed class CheckStuckMissionsHandlerTests
{
    private const ulong AlertChannel = 88;
    private const ulong ClubRole = 77;
    private const ulong ClaimerDiscordId = 66;

    private static readonly Guid ClubA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClubB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly DateTimeOffset ClaimedLongAgo = DateTimeOffset.UtcNow.AddDays(-2);

    private readonly IClubMissionBoardReader _boards = Substitute.For<IClubMissionBoardReader>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IMissionBoardAlertRepository _alerts = Substitute.For<IMissionBoardAlertRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDiscordMessageAccess _messages = Substitute.For<IDiscordMessageAccess>();
    private readonly IDiscordDirectMessageAccess _directMessages = Substitute.For<IDiscordDirectMessageAccess>();

    private readonly MissionBoardAlertsConfiguration _options = new()
    {
        Schedule = "0 0 * ? * * *",
        Enabled = true,
        TextChannelId = AlertChannel,
        OpenClaimMessage = "open {{claimer}}",
        HelpRequestMessage = "help {{claimer}} {{role}}",
        OpenClaimDirectMessage = "dm {{claimer}}"
    };

    private readonly List<(string Text, MessageMentions Mentions)> _posted = [];
    private readonly List<MissionBoardAlert> _recorded = [];

    public CheckStuckMissionsHandlerTests()
    {
        _alerts.ReadSentAlertsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _alerts.When(a => a.Add(Arg.Any<MissionBoardAlert>())).Do(call => _recorded.Add(call.Arg<MissionBoardAlert>()));
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>()).Returns(global::Entities.Club.Create(ClubA, "Alpha", 1));
        _members.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);
        _messages.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _posted.Add((call.ArgAt<string>(0), call.ArgAt<MessageMentions>(2)));
                return 1UL;
            });
        _directMessages.SendDirectMessageAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private Task HandleAsync() =>
        new CheckStuckMissionsHandler(
                _boards,
                _clubs,
                _members,
                _alerts,
                _unitOfWork,
                _messages,
                _directMessages,
                Options.Create(_options),
                new GeoGuessrConfigurationBuilder().WithClub(ClubA, roleId: ClubRole).WithClub(ClubB, isMain: false).BuildOptions(),
                NullLogger<CheckStuckMissionsHandler>.Instance)
            .Handle(new CheckStuckMissionsCommand(), CancellationToken.None);

    private void ClubABoard(params ClubMissionTile[] tiles) =>
        _boards.ReadCurrentAsync(ClubA, Arg.Any<CancellationToken>()).Returns(Week(Board(1, tiles)));

    private static ClubMissionTile OpenClaim(string claimer = "alice") => Tile(claimedBy: claimer, claimedAt: ClaimedLongAgo);

    private static ClubMissionTile HelpRequest(string claimer = "alice") =>
        Tile(claimedBy: claimer, claimedAt: ClaimedLongAgo, helpRequestedAt: ClaimedLongAgo.AddHours(1));

    private void ClaimerIsLinked(string claimer = "alice") =>
        _members.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember>
            {
                [claimer] = new ClubMemberBuilder().WithUserId(claimer).WithNickname("Alice").WithDiscordUserId(ClaimerDiscordId).Build()
            });

    [Fact]
    public async Task Disabled_ChecksNothing()
    {
        _options.Enabled = false;

        await HandleAsync();

        _boards.ReceivedCalls().Should().BeEmpty();
        await _alerts.DidNotReceiveWithAnyArgs().DeleteSentBeforeAsync(default);
    }

    [Fact]
    public async Task WatchesEveryConfiguredClub_ByDefault()
    {
        await HandleAsync();

        await _boards.Received(1).ReadCurrentAsync(ClubA, Arg.Any<CancellationToken>());
        await _boards.Received(1).ReadCurrentAsync(ClubB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WatchesOnlyTheListedClubs_WhenThereAreAny()
    {
        _options.ClubIds = [ClubB];

        await HandleAsync();

        await _boards.DidNotReceive().ReadCurrentAsync(ClubA, Arg.Any<CancellationToken>());
        await _boards.Received(1).ReadCurrentAsync(ClubB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABoardThatCannotBeRead_SendsNothing()
    {
        _boards.ReadCurrentAsync(ClubA, Arg.Any<CancellationToken>()).Returns((ClubMissionBoardWeek?)null);

        await HandleAsync();

        _posted.Should().BeEmpty();
    }

    [Fact]
    public async Task NothingStuck_SendsNothing()
    {
        ClubABoard(Tile(claimedBy: "alice", claimedAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        await HandleAsync();

        _posted.Should().BeEmpty();
        await _alerts.DidNotReceiveWithAnyArgs().ReadSentAlertsAsync(default, default!, default);
    }

    [Fact]
    public async Task OneClubFailing_DoesNotHoldTheOtherBack()
    {
        _boards.ReadCurrentAsync(ClubB, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("boom"));
        ClubABoard(OpenClaim());
        _options.ClubIds = [ClubB, ClubA];

        await HandleAsync();

        _posted.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_StopsTheRun()
    {
        _boards.ReadCurrentAsync(ClubA, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var act = HandleAsync;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DeletesAlertsOnlyAfterTheirBoardWeeksAreGone()
    {
        await HandleAsync();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        await _alerts.Received(1).DeleteSentBeforeAsync(
            Arg.Is<DateTimeOffset>(d => d > cutoff.AddMinutes(-1) && d <= cutoff),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlertsOncePerMissionAndKind()
    {
        var sent = OpenClaim();
        var fresh = OpenClaim("bob");
        ClubABoard(sent, fresh);
        _alerts.ReadSentAlertsAsync(ClubA, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([(sent.MissionId, MissionBoardAlertKind.OpenClaim)]);

        await HandleAsync();

        _posted.Should().ContainSingle().Which.Text.Should().Be("open **bob**");
    }

    [Fact]
    public async Task AnAlertOfAnotherKindForTheSameMission_StillGoesOut()
    {
        var tile = HelpRequest();
        ClubABoard(tile);
        _alerts.ReadSentAlertsAsync(ClubA, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([(tile.MissionId, MissionBoardAlertKind.OpenClaim)]);

        await HandleAsync();

        _posted.Should().ContainSingle();
    }

    [Fact]
    public async Task EverythingAlreadySent_LooksNothingUp()
    {
        var tile = OpenClaim();
        ClubABoard(tile);
        _alerts.ReadSentAlertsAsync(ClubA, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([(tile.MissionId, MissionBoardAlertKind.OpenClaim)]);

        await HandleAsync();

        _posted.Should().BeEmpty();
        await _clubs.DidNotReceiveWithAnyArgs().ReadClubByIdAsync(default, default);
    }

    [Fact]
    public async Task HelpRequest_AsksTheClub_NotTheClaimer()
    {
        ClubABoard(HelpRequest());
        ClaimerIsLinked();

        await HandleAsync();

        var (text, mentions) = _posted.Should().ContainSingle().Subject;
        text.Should().Be($"help **Alice** <@&{ClubRole}>");
        mentions.RoleIds.Should().Equal(ClubRole);
        mentions.UserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task HelpRequest_PingsNoOne_WhenTheRoleMentionIsSwitchedOff()
    {
        _options.MentionClubRoleOnHelpRequest = false;
        ClubABoard(HelpRequest());

        await HandleAsync();

        var (text, mentions) = _posted.Should().ContainSingle().Subject;
        text.Should().Be("help **alice**");
        mentions.Should().Be(MessageMentions.None);
    }

    [Fact]
    public async Task HelpRequest_PingsNoOne_ForAClubWithoutARole()
    {
        _options.ClubIds = [ClubB];
        _boards.ReadCurrentAsync(ClubB, Arg.Any<CancellationToken>()).Returns(Week(Board(1, HelpRequest())));

        await HandleAsync();

        _posted.Should().ContainSingle().Which.Mentions.Should().Be(MessageMentions.None);
    }

    [Fact]
    public async Task OpenClaim_PingsALinkedClaimer()
    {
        ClubABoard(OpenClaim());
        ClaimerIsLinked();

        await HandleAsync();

        var (text, mentions) = _posted.Should().ContainSingle().Subject;
        text.Should().Be($"open <@{ClaimerDiscordId}>");
        mentions.UserIds.Should().Equal(ClaimerDiscordId);
    }

    [Fact]
    public async Task OpenClaim_OnlyNamesTheClaimer_WhenMentioningIsSwitchedOff()
    {
        _options.MentionClaimer = false;
        ClubABoard(OpenClaim());
        ClaimerIsLinked();

        await HandleAsync();

        var (text, mentions) = _posted.Should().ContainSingle().Subject;
        text.Should().Be("open **Alice**");
        mentions.Should().Be(MessageMentions.None);
    }

    [Fact]
    public async Task OpenClaim_NamesAnUnknownClaimer_ByTheirUserId()
    {
        ClubABoard(OpenClaim());

        await HandleAsync();

        _posted.Should().ContainSingle().Which.Text.Should().Be("open **alice**");
    }

    [Fact]
    public async Task WithoutAChannel_PostsNothing_ButStillRecordsTheAlert()
    {
        _options.TextChannelId = null;
        ClubABoard(OpenClaim());

        await HandleAsync();

        _posted.Should().BeEmpty();
        _recorded.Should().ContainSingle();
    }

    [Fact]
    public async Task DirectMessage_GoesToALinkedClaimer_WhenConfigured()
    {
        _options.DmClaimer = true;
        ClubABoard(OpenClaim());
        ClaimerIsLinked();

        await HandleAsync();

        await _directMessages.Received(1).SendDirectMessageAsync(ClaimerDiscordId, "dm Alice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DirectMessage_IsOffByDefault()
    {
        ClubABoard(OpenClaim());
        ClaimerIsLinked();

        await HandleAsync();

        _directMessages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DirectMessage_IsNeverSentForAHelpRequest()
    {
        _options.DmClaimer = true;
        ClubABoard(HelpRequest());
        ClaimerIsLinked();

        await HandleAsync();

        _directMessages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusedDirectMessage_StillCountsAsAlerted()
    {
        _options.DmClaimer = true;
        ClubABoard(OpenClaim());
        ClaimerIsLinked();
        _directMessages.SendDirectMessageAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Forbidden("discord.dm.disabled", "no DMs")));

        await HandleAsync();

        _recorded.Should().ContainSingle();
    }

    [Fact]
    public async Task EachAlertIsStoredAsItGoesOut_SoALaterFailureRepeatsNothing()
    {
        var first = OpenClaim();
        ClubABoard(first, OpenClaim("bob"));
        var sent = 0;
        _messages.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++sent == 1 ? 1UL : throw new HttpRequestException("boom"));

        await HandleAsync();

        _recorded.Should().ContainSingle().Which.MissionId.Should().Be(first.MissionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordsTheClubMissionAndKind()
    {
        var tile = HelpRequest();
        ClubABoard(tile);

        await HandleAsync();

        var alert = _recorded.Should().ContainSingle().Subject;
        alert.ClubId.Should().Be(ClubA);
        alert.MissionId.Should().Be(tile.MissionId);
        alert.Kind.Should().Be(MissionBoardAlertKind.HelpRequest);
    }

    [Fact]
    public async Task UnknownClub_IsCalledByItsId()
    {
        _options.OpenClaimMessage = "{{club}}";
        _options.ClubIds = [ClubB];
        _boards.ReadCurrentAsync(ClubB, Arg.Any<CancellationToken>()).Returns(Week(Board(1, OpenClaim())));

        await HandleAsync();

        _posted.Should().ContainSingle().Which.Text.Should().Be(ClubB.ToString());
    }

    [Fact]
    public void Render_FillsEveryPlaceholder()
    {
        var claimedAt = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var tile = Tile(boardNumber: 3, claimedBy: "alice", claimedAt: claimedAt, title: "Win 2 Duels", currentProgress: 1, targetProgress: 2);
        var board = Week(Board(3, tile, Tile(3, 1, completed: true), Tile(3, 2)));

        var text = CheckStuckMissionsHandler.Render(
            "{{claimer}}|{{mission}}|{{progress}}|{{claimed_at}}|{{board}}|{{remaining}}|{{club}}|{{role}}",
            tile, board, "Alice", "Alpha", 5);

        text.Should().Be($"Alice|Win 2 Duels|1/2|<t:{claimedAt.ToUnixTimeSeconds()}:R>|3|2|Alpha|<@&5>");
    }

    [Fact]
    public void Render_CopesWithAMissingClaimTimeBoardAndRole()
    {
        var tile = Tile(boardNumber: 3, claimedBy: "alice") with { ClaimedAt = null };

        var text = CheckStuckMissionsHandler.Render(" {{claimed_at}} {{remaining}} {{role}} ", tile, Week(Board(1, Tile())), "Alice", "Alpha");

        text.Should().Be("a while ago 0");
    }
}
