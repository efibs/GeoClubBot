using System.Text.Json;
using FluentAssertions;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using UseCases.UseCases.ClubEvents;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

/// <summary>
/// The frames are built the way GeoGuessr's client code reads them (the payload a JSON document
/// encoded as a string, an <c>accessToken</c> beside it); none of them has been captured live yet.
/// </summary>
public sealed class ClubFrameDecoderTests
{
    private static readonly Guid ClubId = Guid.Parse("d0c6b623-8df6-4654-860d-a4757cf78cf4");
    private static readonly string Topic = $"club:{ClubId}";

    private static string Frame(string code, object? payload, string? topic = null, object? timestamp = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["code"] = code,
            ["topic"] = topic ?? Topic,
            ["payload"] = payload is null ? null : payload as string ?? JsonSerializer.Serialize(payload),
            ["accessToken"] = "secret-access-token",
            ["client"] = "web",
            ["timestamp"] = timestamp,
        });

    private static object Member(string userId = "u1", string nick = "Alice", int xp = 0, string joinedAt = "2026-11-07T12:00:00.000Z") => new
    {
        user = new { userId, nick, avatar = "a", isVerified = false },
        role = 0,
        joinedAt,
        xp,
        weeklyXp = 0,
    };

    private static DecodedClubFrame Decode(string frame) => ClubFrameDecoder.Decode(frame, ClubId);

    [Fact]
    public void AJoin_CarriesTheMemberFromThePayload()
    {
        var push = Decode(Frame("ClubMemberJoined", Member(xp: 25))).Should().BeOfType<DecodedClubFrame.Push>().Subject;

        push.PayloadUnusable.Should().BeFalse();
        push.Event.Should().Be(new ClubMemberJoinedPush(
            ClubId, new PushOrder(null, 0), "u1", "Alice", 25, new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void AJoin_ReportsItsFieldSet_WithoutAnyValue()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMemberJoined", Member()));

        push.FieldSet.Should().Be("joinedAt,role,user,user.avatar,user.isVerified,user.nick,user.userId,weeklyXp,xp");
        push.FieldSet.Should().NotContain("Alice");
    }

    [Theory]
    [InlineData("""{"user":{"nick":"Alice"},"joinedAt":"2026-11-07T12:00:00Z","xp":0}""")]
    [InlineData("""{"user":{"userId":"u1"},"joinedAt":"2026-11-07T12:00:00Z","xp":0}""")]
    [InlineData("""{"user":{"userId":"u1","nick":"Alice"},"xp":0}""")]
    [InlineData("""{"user":{"userId":"u1","nick":"Alice"},"joinedAt":"yesterday","xp":0}""")]
    [InlineData("""{"user":{"userId":"u1","nick":"Alice"},"joinedAt":"2026-11-07T12:00:00Z"}""")]
    [InlineData("""{"userId":"u1","nick":"Alice","joinedAt":"2026-11-07T12:00:00Z","xp":0}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void AJoinLackingAnythingTheBotStores_BecomesARosterReRead(string payload)
    {
        var push = Decode(Frame("ClubMemberJoined", payload)).Should().BeOfType<DecodedClubFrame.Push>().Subject;

        push.PayloadUnusable.Should().BeTrue();
        push.Event.Should().BeOfType<ClubRosterChangedPush>().Which.ClubId.Should().Be(ClubId);
    }

    [Fact]
    public void AJoinWithoutPayload_BecomesARosterReRead()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMemberJoined", null));

        push.PayloadUnusable.Should().BeTrue();
        push.FieldSet.Should().BeNull();
    }

    [Fact]
    public void XpGivenAsText_IsStillRead()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMemberJoined",
            """{"user":{"userId":"u1","nick":"Alice"},"joinedAt":"2026-11-07T12:00:00Z","xp":"40"}"""));

        push.Event.Should().BeOfType<ClubMemberJoinedPush>().Which.Xp.Should().Be(40);
    }

    [Fact]
    public void ALeave_NamesTheMember()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMemberLeft", new { userId = "u1" }));

        push.Event.Should().Be(new ClubMemberLeftPush(ClubId, new PushOrder(null, 0), "u1"));
    }

    [Fact]
    public void ALeaveWithoutAUser_BecomesARosterReRead()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMemberLeft", new { friendId = "u1" }));

        push.PayloadUnusable.Should().BeTrue();
        push.Event.Should().BeOfType<ClubRosterChangedPush>();
    }

    [Fact]
    public void AClubUpdate_CarriesLevelAndName_WhenPresent()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubUpdated", new { level = 12, name = "Dragons", joinRule = "Open" }));

        push.Event.Should().Be(new ClubUpdatedPush(ClubId, new PushOrder(null, 0), 12, "Dragons"));
        push.FieldSet.Should().Be("joinRule,level,name");
    }

    [Theory]
    [InlineData("""{"description":"new"}""")]
    [InlineData("""{"level":"high"}""")]
    [InlineData("null")]
    public void AClubUpdateWithoutLevelOrName_ChangesNothing_AndIsNoFallback(string payload)
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubUpdated", payload));

        push.PayloadUnusable.Should().BeFalse();
        push.Event.Should().Be(new ClubUpdatedPush(ClubId, new PushOrder(null, 0), null, null));
    }

    [Fact]
    public void ABoardUpdate_NeedsNoPayload()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMissionBoardUpdated", null));

        push.Event.Should().Be(new ClubMissionBoardUpdatedPush(ClubId, new PushOrder(null, 0)));
    }

    [Theory]
    [InlineData(1762516800000L)]
    [InlineData(1762516800L)]
    [InlineData("2025-11-07T12:00:00Z")]
    public void GeoGuessrsTimestamp_IsKept_InWhicheverFormItComes(object timestamp)
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMissionBoardUpdated", null, timestamp: timestamp));

        push.Event.Order.ServerTime.Should().Be(new DateTimeOffset(2025, 11, 7, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnUnreadableTimestamp_IsLeftOut()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubMissionBoardUpdated", null, timestamp: "soon"));

        push.Event.Order.ServerTime.Should().BeNull();
    }

    [Fact]
    public void AClubEventOfAnotherTopic_IsIgnored()
    {
        Decode(Frame("ClubMemberJoined", Member(), topic: $"club:{Guid.NewGuid()}"))
            .Should().BeOfType<DecodedClubFrame.Ignored>();
    }

    [Fact]
    public void TheTopic_MatchesWhateverCaseGeoGuessrWritesTheClubIdIn()
    {
        Decode(Frame("ClubMissionBoardUpdated", null, topic: Topic.ToUpperInvariant().Replace("CLUB:", "club:")))
            .Should().BeOfType<DecodedClubFrame.Push>();
    }

    [Theory]
    [InlineData("ClubMemberRoleChanged")]
    [InlineData("ClubMemberCameOnline")]
    [InlineData("ClubMemberWentOffline")]
    [InlineData("MissionBoardProgressed")]
    public void ClubEventsTheBotHasNoUseFor_AreIgnored(string code)
    {
        Decode(Frame(code, new { userId = "u1" })).Should().Be(new DecodedClubFrame.Ignored(code));
    }

    [Fact]
    public void ANewCode_IsReportedAsUnknown()
    {
        Decode(Frame("ClubSomethingNew", null)).Should().Be(new DecodedClubFrame.Unknown("ClubSomethingNew"));
    }

    [Fact]
    public void SubscriptionAnswers_AreRecognised()
    {
        Decode(Frame("Subscribed", """{"level":1}""")).Should().Be(new DecodedClubFrame.Subscribed(Topic));
        Decode(Frame("SubscribeDenied", null)).Should().Be(new DecodedClubFrame.SubscribeDenied(Topic));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"topic":"club:x"}""")]
    [InlineData("""{"code":7}""")]
    public void AnythingElse_IsMalformed(string frame)
    {
        Decode(frame).Should().BeOfType<DecodedClubFrame.Malformed>();
    }

    [Fact]
    public void TheAccessToken_IsNeverPartOfWhatIsReported()
    {
        var push = (DecodedClubFrame.Push)Decode(Frame("ClubUpdated", new { level = 3 }));

        push.ToString().Should().NotContain("secret-access-token");
    }
}
