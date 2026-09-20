using Configuration;
using Discord;
using Discord.WebSocket;
using Entities;
using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.UseCases.AI.Feedback;
using Utilities;

namespace GeoClubBot.Services;

/// <summary>
/// Turns 👍/👎 on an answer into a stored verdict, and taking the reaction back into a deletion.
///
/// Sits next to <see cref="AiConversationGateway"/> rather than in the Discord project so it can be
/// registered inside the same AI:Active gate, which is what makes it switch off with the feature.
/// </summary>
public sealed partial class AiFeedbackReactionListener(
    DiscordSocketClient client,
    DiscordBotReadyService botReadyService,
    IServiceScopeFactory scopeFactory,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiFeedbackConfiguration> feedbackConfiguration,
    ILogger<AiFeedbackReactionListener> logger) : IHostedService
{
    /// <summary>
    /// Serialises verdicts on one answer. Two reactions landing together — 👍 then 👎 from the same
    /// person, or two people at once — would otherwise both read no existing row and both insert,
    /// and the unique index would reject the second inside the unit of work, where nothing can
    /// recover from it.
    /// </summary>
    private readonly KeyedAsyncLock<ulong> _messageLocks = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        client.ReactionAdded += OnReactionAdded;
        client.ReactionRemoved += OnReactionRemoved;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.ReactionAdded -= OnReactionAdded;
        client.ReactionRemoved -= OnReactionRemoved;
        return Task.CompletedTask;
    }

    private Task OnReactionAdded(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction) =>
        DispatchAsync(reaction, channel, withdrawn: false);

    private Task OnReactionRemoved(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction) =>
        DispatchAsync(reaction, channel, withdrawn: true);

    private Task DispatchAsync(
        SocketReaction reaction,
        Cacheable<IMessageChannel, ulong> channel,
        bool withdrawn)
    {
        // Discord.Net leaves this null for a channel it has not cached, which is rare for a guild the
        // bot is in but not impossible — and there is nothing to classify without it.
        if (reaction.Channel is null)
        {
            return Task.CompletedTask;
        }

        var signal = new ReactionSignal(
            reaction.Emote.Name,
            reaction.UserId,
            reaction.User is { IsSpecified: true, Value.IsBot: true },
            reaction.Channel is SocketGuildChannel,
            reaction.Channel.Id);

        var rating = AiFeedbackReactionFilter.Classify(
            signal, client.CurrentUser.Id, aiConfiguration.Value, feedbackConfiguration.Value);

        if (rating is not { } verdict)
        {
            return Task.CompletedTask;
        }

        // Never block the gateway task: Discord.Net disconnects a client whose handlers stall.
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleAsync(reaction, channel, verdict, withdrawn).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogUnhandled(logger, reaction.MessageId, exception);
            }
        });

        return Task.CompletedTask;
    }

    private async Task HandleAsync(
        SocketReaction reaction,
        Cacheable<IMessageChannel, ulong> channel,
        AiFeedbackRating rating,
        bool withdrawn)
    {
        using var messageLock = await _messageLocks.AcquireAsync(reaction.MessageId).ConfigureAwait(false);

        using var scope = scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        // The message itself is deliberately never downloaded. No message cache is configured, so
        // every Cacheable here is cold and resolving one would be a REST round trip for each stray
        // reaction in the guild — the id is all the handlers need.
        var result = withdrawn
            ? await mediator.Send(new WithdrawAiAnswerFeedbackCommand(
                reaction.MessageId, reaction.UserId, rating)).ConfigureAwait(false)
            : await mediator.Send(new SubmitAiAnswerFeedbackCommand(
                reaction.MessageId, reaction.UserId, rating, Comment: null)).ConfigureAwait(false);

        if (result.IsFailure)
        {
            // Reactions on messages that are not answers are ordinary channel traffic, so this stays
            // at Debug and the channel hears nothing. A reaction carries no interaction token, so
            // there is no private way to reply and a public one would be noise on every stray click.
            LogRejected(logger, reaction.MessageId, result.Error.Code);
            return;
        }

        await UpdateConfirmationAsync(channel, result.Value, withdrawn).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks the answer as carrying feedback. It says "a verdict is recorded here", not whose: any
    /// number of people can rate the same answer. Worth the call because a click that silently did
    /// nothing — on an answer that has aged out, say — otherwise looks exactly like one that worked.
    /// </summary>
    private async Task UpdateConfirmationAsync(
        Cacheable<IMessageChannel, ulong> channel,
        AiFeedbackOutcome outcome,
        bool withdrawn)
    {
        if (!feedbackConfiguration.Value.ConfirmWithReaction)
        {
            return;
        }

        // Only the transitions matter: the first verdict on an answer and the last one leaving it.
        var adding = !withdrawn && outcome.RatingsOnMessage == 1;
        var removing = withdrawn && outcome.RatingsOnMessage == 0;

        if (!adding && !removing)
        {
            return;
        }

        try
        {
            var resolvedChannel = await channel.GetOrDownloadAsync().ConfigureAwait(false);

            // Fetched by the answer's own id rather than reacting where the click landed: on a split
            // answer those differ, and the confirmation belongs on the message the archive names.
            if (await resolvedChannel.GetMessageAsync(outcome.RatedDiscordMessageId).ConfigureAwait(false)
                is not IUserMessage answer)
            {
                return;
            }

            var confirmation = new Emoji(ConfirmationEmoji);

            if (adding)
            {
                await answer.AddReactionAsync(confirmation).ConfigureAwait(false);
            }
            else
            {
                await answer.RemoveReactionAsync(confirmation, client.CurrentUser).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // The verdict is already stored; failing to decorate the message does not undo it.
            LogConfirmationFailed(logger, outcome.RatedDiscordMessageId, exception);
        }
    }

    /// <summary>Not configurable: it is the bot's own marker, not one of the two votes.</summary>
    private const string ConfirmationEmoji = "✅";

    [LoggerMessage(LogLevel.Debug, "Reaction on message {MessageId} was not recorded as feedback: {ErrorCode}")]
    static partial void LogRejected(ILogger logger, ulong messageId, string errorCode);

    [LoggerMessage(LogLevel.Debug, "Could not update the feedback confirmation on message {MessageId}.")]
    static partial void LogConfirmationFailed(ILogger logger, ulong messageId, Exception exception);

    [LoggerMessage(LogLevel.Error, "Unhandled failure while recording feedback for message {MessageId}.")]
    static partial void LogUnhandled(ILogger logger, ulong messageId, Exception exception);
}
