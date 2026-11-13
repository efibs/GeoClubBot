using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using Utilities;

namespace UseCases.UseCases.AI.ClueOfTheDay;

/// <summary>
/// Posts one lesser-known clue with its picture to the configured channel.
/// </summary>
/// <param name="ChannelId">A channel to post to instead of the configured one, for an admin trying it out; null posts to the configured one.</param>
public sealed record PostClueOfTheDayCommand(ulong? ChannelId = null) : ICommand<Result<CluePosted>>;

/// <param name="MessageId">The message posted.</param>
/// <param name="Title">What the clue was headed: its guide and section.</param>
/// <param name="SourceUrl">The guide it came from.</param>
public sealed record CluePosted(ulong MessageId, string Title, string SourceUrl);

public static class ClueOfTheDayErrorCodes
{
    public const string Disabled = "ai.clue_of_the_day_disabled";
    public const string NoChannel = "ai.clue_of_the_day_no_channel";
    public const string NothingToPost = "ai.clue_of_the_day_nothing_to_post";
    public const string NotPosted = "ai.clue_of_the_day_not_posted";
}

/// <summary>
/// Draws picture clues the way a question for "a clue I don't know yet" does (<see cref="ClueSampler"/>:
/// random picture chunks of the guides other than PlonkIt, with words of their own, one per guide),
/// takes the first not posted within <see cref="AiClueOfTheDayConfiguration.NoRepeatDays"/>, and posts
/// it like an answer: the heading, the guide's words, the picture uploaded, the guide cited beneath.
/// Costs no AI request. The post is recorded whether or not the message went out, so a failed post
/// does not make the same clue the next day's.
/// </summary>
public sealed partial class PostClueOfTheDayHandler(
    ClueSampler clueSampler,
    IAiCluePostRepository posts,
    IAiChannelPublisher publisher,
    IOptions<AiClueOfTheDayConfiguration> configuration,
    IOptions<AiConfiguration> aiConfiguration,
    TimeProvider timeProvider,
    ILogger<PostClueOfTheDayHandler> logger)
    : IRequestHandler<PostClueOfTheDayCommand, Result<CluePosted>>
{
    /// <summary>Longest text posted of a clue; a slide's notes can run to pages.</summary>
    public const int MaxClueCharacters = 900;

    public async Task<Result<CluePosted>> Handle(PostClueOfTheDayCommand request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;
        if (!aiConfiguration.Value.Active)
        {
            return Error.Validation(ClueOfTheDayErrorCodes.Disabled, "The AI features are off (AI:Active).");
        }

        // An admin's own channel works while the schedule is off; the schedule needs the setting on.
        if (request.ChannelId is null && !settings.Enabled)
        {
            return Error.Validation(ClueOfTheDayErrorCodes.Disabled, "The clue of the day is turned off (AI:ClueOfTheDay:Enabled).");
        }

        var channelId = request.ChannelId ?? settings.TextChannelId;
        if (channelId is null)
        {
            return Error.Validation(ClueOfTheDayErrorCodes.NoChannel, "No channel is configured for the clue of the day (AI:ClueOfTheDay:TextChannelId).");
        }

        var now = timeProvider.GetUtcNow();
        var recent = await posts.ReadChunkIdsPostedSinceAsync(now - TimeSpan.FromDays(settings.NoRepeatDays), cancellationToken)
            .ConfigureAwait(false);

        var drawn = await clueSampler.DrawAsync(Math.Max(1, settings.Draws), settings.ExcludeMainGuideClues, cancellationToken)
            .ConfigureAwait(false);

        var clue = drawn.FirstOrDefault(hit => !hit.ChunkIds.Any(recent.Contains));
        if (clue is null)
        {
            LogNothingToPost(logger, drawn.Count, recent.Count);
            return Error.NotFound(ClueOfTheDayErrorCodes.NothingToPost,
                drawn.Count == 0
                    ? "No picture clue could be drawn from the index."
                    : $"Every clue drawn was posted within the last {settings.NoRepeatDays} days; try again, or lower AI:ClueOfTheDay:NoRepeatDays.");
        }

        var answer = ClueOfTheDayPost.Build(clue, settings.Heading, MaxClueCharacters);

        var messageId = await publisher.PostAsync(channelId.Value, answer, cancellationToken).ConfigureAwait(false);

        posts.Add(AiCluePost.Create(clue.Id, clue.SourceUrl, channelId.Value, messageId, now));

        if (messageId is null)
        {
            LogNotPosted(logger, channelId.Value, clue.SourceUrl);
            return Error.Unexpected(ClueOfTheDayErrorCodes.NotPosted, "The clue could not be posted to the channel; it is not offered again for a while.");
        }

        LogPosted(logger, channelId.Value, clue.SourceUrl);
        return new CluePosted(messageId.Value, answer.Images.Count > 0 ? answer.Images[0].Title ?? string.Empty : string.Empty, clue.SourceUrl);
    }

    [LoggerMessage(LogLevel.Information, "Posted the clue of the day to channel {ChannelId}: {SourceUrl}")]
    static partial void LogPosted(ILogger logger, ulong channelId, string sourceUrl);

    [LoggerMessage(LogLevel.Warning, "The clue of the day could not be posted to channel {ChannelId} ({SourceUrl}); it is recorded so it is not drawn again soon.")]
    static partial void LogNotPosted(ILogger logger, ulong channelId, string sourceUrl);

    [LoggerMessage(LogLevel.Warning, "No clue of the day to post: {Drawn} drawn, {Recent} posted recently.")]
    static partial void LogNothingToPost(ILogger logger, int drawn, int recent);
}
