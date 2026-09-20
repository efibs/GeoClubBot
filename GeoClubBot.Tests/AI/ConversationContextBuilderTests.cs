using Configuration;
using Entities;
using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Pins the rules that decide what the model is told about a conversation. The branching cases are
/// the reason the tree is stored as parent edges at all, so they are asserted explicitly rather than
/// left to emerge.
/// </summary>
public sealed class ConversationContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private const ulong Bot = 1;
    private const ulong UserA = 10;
    private const ulong UserB = 20;

    [Fact]
    public void Build_GivesSiblingBranchesDisjointHistories_SharingOnlyTheCommonPrefix()
    {
        // The case that motivates the whole design: two people reply to the same bot answer. Each
        // must see the shared prefix and their own follow-ups, and neither may see the other's.
        //
        //   100 userA asks
        //   └─ 101 bot answers
        //       ├─ 102 userA follows up   -> 103 bot
        //       └─ 104 userB follows up   -> 105 bot
        var turns = new[]
        {
            User(100, null, 100, UserA, "what country is this pole from?", depth: 0),
            Assistant(101, 100, 100, "Looks Ghanaian.", depth: 1),
            User(102, 101, 100, UserA, "and the wires?", depth: 2),
            Assistant(103, 102, 100, "Three-phase, usually.", depth: 3),
            User(104, 101, 100, UserB, "what about Kenya?", depth: 2),
            Assistant(105, 104, 100, "Kenya differs.", depth: 3)
        };

        var branchA = ConversationContextBuilder.Build(turns, parentMessageId: 103, Limits(), Now);
        var branchB = ConversationContextBuilder.Build(turns, parentMessageId: 105, Limits(), Now);

        branchA.Turns.Select(t => t.Content).Should().Equal(
            "what country is this pole from?", "Looks Ghanaian.", "and the wires?", "Three-phase, usually.");

        branchB.Turns.Select(t => t.Content).Should().Equal(
            "what country is this pole from?", "Looks Ghanaian.", "what about Kenya?", "Kenya differs.");

        branchA.Turns.Should().NotContain(t => t.Content.Contains("Kenya"));
        branchB.Turns.Should().NotContain(t => t.Content.Contains("wires"));
    }

    [Fact]
    public void Build_LetsSomeoneJoinAnotherPersonsBranch()
    {
        // Replying into an existing branch is a legitimate way for a third party to join the dig, and
        // they should get that branch's full history, not a fresh start.
        var turns = new[]
        {
            User(100, null, 100, UserA, "question", depth: 0),
            Assistant(101, 100, 100, "answer", depth: 1),
            User(102, 101, 100, UserA, "follow up", depth: 2),
            Assistant(103, 102, 100, "second answer", depth: 3)
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 103, Limits(), Now);

        context.Turns.Should().HaveCount(4);
        context.Turns.Select(t => t.AuthorDiscordUserId).Should().Contain(UserA);
    }

    [Fact]
    public void Build_StartsFresh_WhenTheParentIsUnknown()
    {
        // Replying to a message that aged out of retention, or to some unrelated message. Answering
        // fresh is friendlier than refusing.
        var turns = new[] { User(100, null, 100, UserA, "question", depth: 0) };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 999, Limits(), Now);

        context.IsNewConversation.Should().BeTrue();
        context.Turns.Should().BeEmpty();
    }

    [Fact]
    public void Build_StartsFresh_WhenTheBranchHasGoneIdle()
    {
        var turns = new[]
        {
            User(100, null, 100, UserA, "question", depth: 0, createdAt: Now.AddDays(-3)),
            Assistant(101, 100, 100, "answer", depth: 1, createdAt: Now.AddDays(-3))
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 101, Limits(), Now);

        context.IsNewConversation.Should().BeTrue();
    }

    [Fact]
    public void Build_KeepsAnOldButStillActiveThread()
    {
        // Idle time is measured from the branch's last message, not the root. Measuring from the root
        // would silently wipe the context of a long discussion that is still going.
        var turns = new[]
        {
            User(100, null, 100, UserA, "asked three days ago", depth: 0, createdAt: Now.AddDays(-3)),
            Assistant(101, 100, 100, "answered then", depth: 1, createdAt: Now.AddDays(-3)),
            User(102, 101, 100, UserA, "still going", depth: 2, createdAt: Now.AddMinutes(-5)),
            Assistant(103, 102, 100, "still answering", depth: 3, createdAt: Now.AddMinutes(-4))
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 103, Limits(), Now);

        context.IsNewConversation.Should().BeFalse();
        context.Turns.Should().HaveCount(4);
    }

    [Fact]
    public void Build_DropsTheOldestTurns_WhenThePathExceedsTheTurnLimit()
    {
        var turns = Chain(depth: 10);

        var context = ConversationContextBuilder.Build(turns, ParentOf(turns), Limits(maxTurns: 4), Now);

        context.Turns.Should().HaveCount(4);
        context.WasTrimmed.Should().BeTrue();
        context.Turns[^1].Content.Should().Be("turn-9", "the newest turns are the ones worth keeping");
        context.Turns[0].Content.Should().Be("turn-6");
    }

    [Fact]
    public void Build_DropsTheOldestTurns_WhenTheCharacterBudgetIsExceeded()
    {
        var turns = new[]
        {
            User(100, null, 100, UserA, new string('a', 400), depth: 0),
            Assistant(101, 100, 100, new string('b', 400), depth: 1),
            User(102, 101, 100, UserA, new string('c', 100), depth: 2)
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 102, Limits(maxCharacters: 600), Now);

        context.WasTrimmed.Should().BeTrue();
        context.Turns.Should().HaveCount(2, "the oldest turn is dropped until the budget fits");
        context.Turns[0].Content.Should().StartWith("b");
    }

    [Fact]
    public void Build_NeverTrimsAwayTheMostRecentTurn()
    {
        // Even an over-budget single turn must survive: dropping it would leave the model with no
        // idea what was being discussed.
        var turns = new[] { User(100, null, 100, UserA, new string('a', 5_000), depth: 0) };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 100, Limits(maxCharacters: 10), Now);

        context.Turns.Should().ContainSingle();
    }

    [Fact]
    public void Build_KeepsOnlyTheNewestImages()
    {
        // Images cost far more than the text around them, so old ones are dropped while their text
        // is kept.
        var turns = new[]
        {
            User(100, null, 100, UserA, "first", ["https://img/1.png"], depth: 0),
            User(101, 100, 100, UserA, "second", ["https://img/2.png"], depth: 1),
            User(102, 101, 100, UserA, "third", ["https://img/3.png"], depth: 2)
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 102, Limits(maxImages: 2), Now);

        context.Turns.Should().HaveCount(3, "the text of older turns is kept even when their images are not");
        context.Turns.SelectMany(t => t.ImageUrls).Should().Equal("https://img/2.png", "https://img/3.png");
        context.Turns[0].ImageUrls.Should().BeEmpty();
    }

    [Fact]
    public void Build_DropsEveryImage_WhenNoneAreAllowed()
    {
        var turns = new[] { User(100, null, 100, UserA, "look", ["https://img/1.png"], depth: 0) };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 100, Limits(maxImages: 0), Now);

        context.Turns.Should().ContainSingle();
        context.Turns[0].ImageUrls.Should().BeEmpty();
    }

    [Fact]
    public void Build_ReturnsAPartialPrefix_WhenAMiddleLinkIsMissing()
    {
        // Retention can remove the start of a long conversation while leaving the tail. Whatever
        // remains is still useful history.
        var turns = new[]
        {
            User(102, 101, 100, UserA, "orphaned start", depth: 2),
            Assistant(103, 102, 100, "answer", depth: 3)
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 103, Limits(), Now);

        context.Turns.Select(t => t.Content).Should().Equal("orphaned start", "answer");
    }

    [Fact]
    public void Build_TerminatesOnACyclicParentChain()
    {
        // Defensive: a cycle should never exist, but walking one forever would hang the bot.
        var turns = new[]
        {
            User(100, 101, 100, UserA, "a", depth: 0),
            Assistant(101, 100, 100, "b", depth: 1)
        };

        var context = ConversationContextBuilder.Build(turns, parentMessageId: 101, Limits(), Now);

        context.Turns.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ReportsTheParentDepth_SoLongThreadsCanBeFlagged()
    {
        var turns = Chain(depth: 6);

        var context = ConversationContextBuilder.Build(turns, ParentOf(turns), Limits(), Now);

        context.ParentDepth.Should().Be(5);
    }

    [Fact]
    public void Build_HandlesAnEmptyStore()
    {
        ConversationContextBuilder.Build([], parentMessageId: 1, Limits(), Now)
            .IsNewConversation.Should().BeTrue();
    }

    [Fact]
    public void Build_CarriesTheExistingRoot_NotTheMessageBeingRepliedTo()
    {
        // The two coincide only on the first follow-up. Deriving the root from the parent message id
        // re-roots the tree at every exchange, and a conversation then lives in the database as a
        // chain of two-turn fragments — which truncates the next reply's history and anything that
        // later archives the branch.
        var turns = Chain(4);

        var context = ConversationContextBuilder.Build(turns, ParentOf(turns), Limits(), Now);

        context.ConversationId.Should().Be(100, "every turn of this tree is rooted at message 100");
        context.ConversationId.Should().NotBe(ParentOf(turns));
    }

    [Fact]
    public void Build_ReportsNoRoot_WhenTheReplyStartsAFreshConversation()
    {
        // Idle past the window. The caller roots the new conversation at the incoming message, which
        // it can only do if the context says there is no existing root to keep.
        var stale = Chain(2);

        var context = ConversationContextBuilder.Build(stale, ParentOf(stale), Limits(maxIdleHours: 0), Now);

        context.IsNewConversation.Should().BeTrue();
        context.ConversationId.Should().BeNull();
    }

    [Fact]
    public void BuildBranch_ReturnsTheWholeAncestorPath_IgnoringTheContextLimits()
    {
        // The archive keeps the branch as it was rated. MaxTurns and MaxContextCharacters exist to
        // make history fit a model's context window, which has nothing to do with what is worth
        // storing, so BuildBranch must not honour them.
        var turns = Chain(10);

        var branch = ConversationContextBuilder.BuildBranch(turns, ParentOf(turns));

        branch.Should().HaveCount(10);
        branch.Select(turn => turn.Content).Should().ContainInOrder("turn-0", "turn-1", "turn-9");
    }

    [Fact]
    public void BuildBranch_IgnoresTheIdleWindow()
    {
        // A verdict can arrive days after the answer. Applying the staleness rule here would refuse
        // to archive exactly the old, forgotten answers most worth reviewing.
        var question = User(100, null, 100, UserA, "q", createdAt: Now.AddDays(-30));
        var answer = Assistant(101, 100, 100, "a", depth: 1, createdAt: Now.AddDays(-30));

        var branch = ConversationContextBuilder.BuildBranch([question, answer], 101);

        branch.Should().HaveCount(2);
    }

    [Fact]
    public void BuildBranch_ExcludesSiblingBranches()
    {
        //   100 userA asks -> 101 bot -> { 102 userA, 104 userB }
        var turns = new[]
        {
            User(100, null, 100, UserA, "root"),
            Assistant(101, 100, 100, "answer", depth: 1),
            User(102, 101, 100, UserA, "mine", depth: 2),
            Assistant(103, 102, 100, "to mine", depth: 3),
            User(104, 101, 100, UserB, "theirs", depth: 2),
            Assistant(105, 104, 100, "to theirs", depth: 3)
        };

        var branch = ConversationContextBuilder.BuildBranch(turns, 103);

        branch.Select(turn => turn.Content).Should().Equal("root", "answer", "mine", "to mine");
    }

    [Fact]
    public void BuildBranch_ReturnsEmpty_WhenTheLeafIsUnknown()
    {
        ConversationContextBuilder.BuildBranch(Chain(4), leafMessageId: 999).Should().BeEmpty();
    }

    [Fact]
    public void BuildBranch_ReturnsEmpty_ForAnEmptyConversation()
    {
        ConversationContextBuilder.BuildBranch([], leafMessageId: 100).Should().BeEmpty();
    }

    [Fact]
    public void BuildBranch_Terminates_WhenTheStoredTreeHasACycle()
    {
        // A malformed parent edge must not hang the walk; the same guard Build relies on.
        var first = User(100, 101, 100, UserA, "a");
        var second = Assistant(101, 100, 100, "b", depth: 1);

        var branch = ConversationContextBuilder.BuildBranch([first, second], 101);

        branch.Should().HaveCount(2);
    }

    private static AiConversationConfiguration Limits(
        int maxTurns = 12,
        int maxCharacters = 12_000,
        int maxImages = 2,
        int maxIdleHours = 24) =>
        new()
        {
            MaxTurns = maxTurns,
            MaxContextCharacters = maxCharacters,
            MaxImagesInContext = maxImages,
            MaxIdleHours = maxIdleHours
        };

    /// <summary>A straight, unbranched chain of alternating turns.</summary>
    private static AiConversationTurn[] Chain(int depth) =>
        [.. Enumerable.Range(0, depth).Select(i => i % 2 == 0
            ? User((ulong)(100 + i), i == 0 ? null : (ulong)(99 + i), 100, UserA, $"turn-{i}", depth: i)
            : Assistant((ulong)(100 + i), (ulong)(99 + i), 100, $"turn-{i}", depth: i))];

    private static ulong ParentOf(AiConversationTurn[] turns) => turns[^1].DiscordMessageId;

    private static AiConversationTurn User(
        ulong messageId,
        ulong? parentId,
        ulong conversationId,
        ulong authorId,
        string content,
        IEnumerable<string>? images = null,
        int depth = 0,
        DateTimeOffset? createdAt = null) =>
        AiConversationTurn.CreateUserTurn(messageId, parentId, conversationId, channelId: 5, guildId: 7,
            authorId, content, images, depth, createdAt ?? Now.AddMinutes(-1));

    private static AiConversationTurn Assistant(
        ulong messageId,
        ulong parentId,
        ulong conversationId,
        string content,
        int depth = 0,
        DateTimeOffset? createdAt = null) =>
        AiConversationTurn.CreateAssistantTurn(messageId, parentId, conversationId, channelId: 5, guildId: 7,
            Bot, content, modelId: "test/model", retrievedSourceUrls: null, citedSourceUrls: null,
            chunkMessageIds: null, depth, createdAt ?? Now.AddMinutes(-1));
}
