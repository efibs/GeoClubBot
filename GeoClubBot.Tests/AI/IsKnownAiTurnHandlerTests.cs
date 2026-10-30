using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class IsKnownAiTurnHandlerTests
{
    private const ulong MessageId = 42;

    private readonly IAiConversationRepository _conversations = Substitute.For<IAiConversationRepository>();

    [Fact]
    public async Task StoredMessage_IsKnown()
    {
        _conversations.ReadByMessageIdAsync(MessageId, Arg.Any<CancellationToken>())
            .Returns(AiConversationTurn.CreateUserTurn(
                MessageId, null, 1, channelId: 5, guildId: 7, 9, "hi", null, 0, DateTimeOffset.UnixEpoch));

        var known = await new IsKnownAiTurnHandler(_conversations)
            .Handle(new IsKnownAiTurnQuery(MessageId), CancellationToken.None);

        known.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownMessage_IsNot()
    {
        _conversations.ReadByMessageIdAsync(MessageId, Arg.Any<CancellationToken>()).Returns((AiConversationTurn?)null);

        var known = await new IsKnownAiTurnHandler(_conversations)
            .Handle(new IsKnownAiTurnQuery(MessageId), CancellationToken.None);

        known.Should().BeFalse();
    }
}
