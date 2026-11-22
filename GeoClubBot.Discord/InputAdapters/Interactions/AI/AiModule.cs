using System.Globalization;
using System.Text;
using Configuration;
using Constants;
using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.AI.ClueOfTheDay;
using UseCases.UseCases.AI.Feedback;
using UseCases.UseCases.AI.Ingestion;
using UseCases.UseCases.AI.KnowledgeGaps;
using UseCases.UseCases.AI.Progress;
using UseCases.UseCases.AI.Quiz;
using UseCases.UseCases.AI.Review;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

[CommandContextType(InteractionContextType.Guild)]
[HelpFeature("ai", Emoji = "🤖", Title = "AI assistant", Order = 90, ShownWhen = HelpToggle.Ai,
    Summary = "Ask about GeoGuessr metas and I answer from community guides, with their pictures.")]
[HelpSection(1, "Asking a question", """
    No command needed: **@-mention me** with your question, e.g. `@GeoClubBot what do Ghanaian bollards look like?`. I answer in the channel, so everyone learns, cite the guides I used and attach their pictures. Anything from general knowledge instead starts with "Not from the guides:". A hard question can take up to a minute.
    - **A screenshot:** attach it and ask about it.
    - **A plant:** ask "what tree is this?" with a screenshot, ideally a close crop (several pictures of one plant work too). Say where it was taken ("just south of Boston", a country, a Maps link) and I weigh what grows there.
    - **A place:** paste a Google Maps Street View link and ask what gives it away; I check which clues its view shows.
    """)]
[HelpSection(2, "Follow-ups", """
    **Reply to my answer**; no mention needed. I remember the conversation and the guides I cited, so "show me [2] bigger" works. Each reply starts its own branch, so your follow-ups and someone else's never mix. A conversation ends after a day of quiet.
    """)]
[HelpSection(3, "Rating answers", """
    Click 👍 or 👎 under an answer; click it again to take it back. Rated conversations are kept to improve the answers, unrated ones are deleted after a while.
    """)]
[Group("ai", "The AI assistant: guide search, game reviews, the quiz")]
public partial class AiModule(IServiceProvider serviceProvider, ISender mediator, ILogger<AiModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    /// <summary>Hits shown by /ai search; enough to judge retrieval without flooding the reply.</summary>
    private const int SearchResultLimit = 5;

    [SlashCommand("status", "Which AI models are available, how much is indexed, and today's allowance")]
    [HelpDetail("The AI runs on a free daily allowance that resets at 00:00 UTC, with an hourly cap per person, so \"out of requests\" is expected rather than broken. **Picture links** says whether my linked pictures load.")]
    public Task StatusAsync() =>
        ExecuteAsync(async ct =>
        {
            if (_catalog is null || _knowledgeIndex is null)
            {
                await FollowupAsync("AI features are not active.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Started first, because it may wait out its timeout and nothing else here depends on it.
            var pictureLinks = _imageRelay?.CheckPublicReachabilityAsync(ct);

            var status = _catalog.ReadStatus();
            var chain = await _catalog.ReadChainAsync(new ChatModelRequirements(), ct).ConfigureAwait(false);
            var sources = await Mediator.Send(new ReadKnowledgeSourceStatusQuery(), ct).ConfigureAwait(false);

            var builder = new EmbedBuilder()
                .WithTitle("🤖 AI status")
                .AddField("Free models known", $"{status.ModelCount} ({status.VisionModelCount} accept images)", inline: true)
                .AddField("Catalog", status.Source.ToString(), inline: true)
                .AddField("Refreshed", status.LastRefreshedAtUtc is { } at
                    ? TimestampTag.FromDateTimeOffset(at, TimestampTagStyles.Relative).ToString()
                    : "never", inline: true)
                .AddField("Model chain", $"`{string.Join("` → `", chain)}`");

            // Shown so a runtime exclusion is noticed without reading logs: it is forgotten on restart,
            // and each one is a candidate for the configured block list. Capped well inside Discord's
            // 1024-character field limit; a longer list is in the logs.
            if (status.LearnedUnfitModelIds.Count > 0)
            {
                const int maxListed = 8;
                var listed = string.Join("`, `", status.LearnedUnfitModelIds.Take(maxListed));
                var more = status.LearnedUnfitModelIds.Count > maxListed
                    ? $" and {status.LearnedUnfitModelIds.Count - maxListed} more"
                    : string.Empty;

                builder.AddField("Excluded until restart",
                    $"`{listed}`{more} — add to `AI:OpenRouter:BlockedModelIds` to keep them out");
            }

            var embed = builder
                .AddField("Indexed chunks", await ReadIndexSizeAsync(ct).ConfigureAwait(false), inline: true)
                .AddField("Sources", sources switch
                {
                    { IsFailure: true } => "unavailable",
                    { Value.Total: 0 } => "none catalogued — run `/ai sync-sources`",
                    var ok => AiSourcesFormatter.DescribeCounts(ok.Value)
                })
                .AddField(AiImageRelayStatusFormatter.FieldName, AiImageRelayStatusFormatter.Describe(
                    pictureLinks is null ? null : await pictureLinks.ConfigureAwait(false)))
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to read the AI status.");

    [SlashCommand("search", "What the guide library holds on a topic, without asking a model")]
    [HelpDetail("Handy for finding the guide itself, or checking whether I know anything about a topic.")]
    public Task SearchAsync(
        [Summary(description: "What to look for")] string query,
        [Summary(description: "Only results from one country")] string? country = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new SearchKnowledgeQuery(query, country, SearchResultLimit), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            if (result.Value.Count == 0)
            {
                await FollowupAsync("Nothing in the index matched that.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(FormatHits(result.Value), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to search the guide index.");

    [SlashCommand("quiz", "A guide picture with four answers to pick from; everyone guesses once and scores points")]
    [HelpShownWhen(HelpToggle.AiQuiz)]
    [HelpDetail("Easy, medium and hard questions score 1, 2 and 3 points. The answer shows after a while, or when whoever started it reveals it.")]
    public Task QuizAsync(
        [Summary(description: "Only pictures of one country, e.g. Vietnam: then it asks where in it, or what is typical")] string? country = null,
        [Summary(description: "How hard; easy or medium if left out")]
        [Choice("easy (1 point)", "easy")]
        [Choice("medium (2 points)", "medium")]
        [Choice("hard (3 points)", "hard")]
        string? difficulty = null,
        [Summary(name: "lesser-known", description: "Only clues PlonkIt does not have")] bool lesserKnown = false) =>
        ExecuteAsync(ct => PostQuizAsync(country, lesserKnown, ParseDifficulty(difficulty), ct),
            failureMessage: "The quiz could not draw a picture just now.");

    [SlashCommand("quiz-scores", "The quiz leaderboard: points over all time and over the last days")]
    [HelpShownWhen(HelpToggle.AiQuiz)]
    public Task QuizScoresAsync() =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new ReadQuizLeaderboardQuery(Context.User.Id), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(AiQuizFormatter.Leaderboard(result.Value), ephemeral: true, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "The quiz leaderboard could not be read just now.");

    // One wildcard for all filters: Discord.Net's wildcard needs at least one character, so a separate
    // one for the country never matched a quiz played without a country (#479).
    [ComponentInteraction($"{ComponentIds.AiQuizAgainButtonId}:*", ignoreGroupNames: true)]
    public Task QuizAgainAsync(string filters)
    {
        var (country, lesserKnown, difficulty) = AiQuizFormatter.ReadAgainFilters(filters);
        return ExecuteAsync(ct => PostQuizAsync(country, lesserKnown, difficulty, ct),
            failureMessage: "The quiz could not draw a picture just now.");
    }

    [ComponentInteraction($"{ComponentIds.AiQuizPickButtonId}:*:*", ignoreGroupNames: true)]
    public Task QuizPickAsync(string roundId, string option) =>
        ExecuteAsync(async ct =>
        {
            if (AiQuizFormatter.ReadPick(roundId, option) is not { } pick)
            {
                await FollowupAsync("That button is no answer I know.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator.Send(new GuessQuizCommand(pick.RoundId, Context.User.Id, pick.Option), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(AiQuizFormatter.Verdict(result.Value), ephemeral: true, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            await UpdateQuizMessageAsync(result.Value.Round).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Your guess could not be recorded just now.");

    [ComponentInteraction($"{ComponentIds.AiQuizRevealButtonId}:*", ignoreGroupNames: true)]
    public Task QuizRevealAsync(string roundId) =>
        ExecuteAsync(async ct =>
        {
            if (!Guid.TryParseExact(roundId, "N", out var id))
            {
                await FollowupAsync("That button is no question I know.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var isAdmin = Context.User is IGuildUser member
                          && (member.GuildPermissions.Administrator || member.GuildPermissions.ManageMessages);
            var result = await Mediator.Send(new RevealQuizRoundCommand(id, Context.User.Id, isAdmin), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await UpdateQuizMessageAsync(result.Value).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "The answer could not be revealed just now.");

    // The self-judged buttons of the quiz before #491, still under old messages.
    [ComponentInteraction(ComponentIds.AiQuizRightButtonId, ignoreGroupNames: true)]
    public Task QuizRightAsync() => ExecuteAsync(_ => AnswerOldQuizAsync(), ephemeral: true);

    [ComponentInteraction(ComponentIds.AiQuizWrongButtonId, ignoreGroupNames: true)]
    public Task QuizWrongAsync() => ExecuteAsync(_ => AnswerOldQuizAsync(), ephemeral: true);

    private Task AnswerOldQuizAsync() =>
        FollowupAsync("That question is from the old quiz, which kept no score. Press *Another*, or run `/ai quiz`, for one with points.",
            ephemeral: true);

    private static AiQuizDifficulty? ParseDifficulty(string? difficulty) =>
        Enum.TryParse<AiQuizDifficulty>(difficulty, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>Shows a round's state in its message: the running count while open, the answer once revealed.</summary>
    private async Task UpdateQuizMessageAsync(AiQuizRound round)
    {
        if (Context.Interaction is not IComponentInteraction { Message: { } message })
        {
            return;
        }

        try
        {
            await message.ModifyAsync(properties =>
            {
                properties.Content = round.IsRevealed ? AiQuizFormatter.Revealed(round) : AiQuizFormatter.Question(round);
                properties.Components = AiQuizFormatter.Buttons(round);
                properties.AllowedMentions = AllowedMentions.None;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The guess is recorded either way; a count that lags one guess behind is no reason to fail it.
            LogQuizMessageNotUpdated(logger, exception, message.Id);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Could not update quiz message {MessageId}.")]
    static partial void LogQuizMessageNotUpdated(ILogger logger, Exception exception, ulong messageId);

    /// <summary>
    /// Draws a question and posts it with its picture: uploaded from the relay when the picture is a
    /// relayed one, so it shows whether or not Discord can reach this host; linked otherwise. The round is
    /// recorded before posting, and the message it went out as after.
    /// </summary>
    private async Task PostQuizAsync(string? country, bool lesserKnown, AiQuizDifficulty? difficulty, CancellationToken cancellationToken)
    {
        var quiz = serviceProvider.GetRequiredService<IOptions<AiQuizConfiguration>>().Value;
        var ai = serviceProvider.GetRequiredService<IOptions<AiConfiguration>>().Value;
        if (quiz.OnlyInAllowedChannels && ai.AllowedChannelIds.Count > 0 && !ai.AllowedChannelIds.Contains(Context.Channel.Id))
        {
            await FollowupAsync("The quiz is only played in the channels the assistant answers in.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var result = await Mediator.Send(
                new StartQuizRoundCommand(Context.Channel.Id, Context.User.Id, country, lesserKnown, difficulty),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            await FollowupFailureAsync(result.Error).ConfigureAwait(false);
            return;
        }

        var round = result.Value;
        var text = AiQuizFormatter.Question(round);
        var buttons = AiQuizFormatter.Buttons(round);

        IUserMessage posted;
        var imageRelay = serviceProvider.GetRequiredService<IImageRelay>();
        if (RelayedImageUrl.TryReadName(round.ImageUrl, out var storedName)
            && await imageRelay.ReadAsync(storedName, cancellationToken).ConfigureAwait(false) is { } stored)
        {
            await using (stored.Content.ConfigureAwait(false))
            {
                var extension = RelayedImageUrl.TrySplitName(storedName, out _, out var ext) && !string.IsNullOrEmpty(ext) ? ext : ".jpg";
                posted = await FollowupWithFileAsync(stored.Content, $"quiz{extension}", text, components: buttons, allowedMentions: AllowedMentions.None)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            var embed = new EmbedBuilder().WithImageUrl(round.ImageUrl).Build();
            posted = await FollowupAsync(text, embed: embed, components: buttons, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }

        await Mediator.Send(new AttachQuizMessageCommand(round.Id, posted.Id), cancellationToken).ConfigureAwait(false);
    }

    [SlashCommand("review", "Review a game or a Street View place: the clues each round showed, and what to study")]
    [HelpShownWhen(HelpToggle.AiRoundReview)]
    [HelpDetail("""
        Round by round: the clues that could be seen and point to the country and region, and for a missed round the easiest clue that tells the right country from your guess. It ends with what to practise first, and each round's view links to Google Maps. A vision model checks what a round showed and can be wrong; the review says so.
        - a **challenge** link: your game in it (needs your account linked); empty for the club's latest daily challenge you played
        - a **game** link (`/game/…` or `/results/…`): anyone's finished game
        - a **Street View** link: what gives that place away
        """)]
    public Task ReviewAsync(
        [Summary(description: "A challenge, game or Google Maps Street View link; empty for the latest daily challenge")] string? link = null) =>
        ExecuteAsync(async ct =>
        {
            // A review reads a game, the guides and every panorama before the model starts, so its steps
            // are shown in the command's own ephemeral reply while it runs (#481).
            var progress = new AiProgressLog();
            using var tracking = AiProgress.Track(progress);
            await using var reporter = AiProgressReporter.Start(
                progress,
                new InteractionProgressDisplay(Context.Interaction),
                serviceProvider.GetRequiredService<IOptions<AiProgressConfiguration>>().Value,
                serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System,
                logger);

            var result = await Mediator.Send(new ReviewRoundsCommand(Context.User.Id, link), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await StopReportingAsync(reporter).ConfigureAwait(false);
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var review = result.Value;
            ulong? posted;
            using (AiProgress.Step(AiProgressKind.Posting))
            {
                posted = await serviceProvider.GetRequiredService<IAiChannelPublisher>()
                    .PostAsync(Context.Channel.Id, review.Answer, ct).ConfigureAwait(false);
            }

            await StopReportingAsync(reporter).ConfigureAwait(false);

            await FollowupAsync(
                posted is null
                    ? "The review could not be posted here."
                    : review.Subject.Kind == ReviewSubjectKind.StreetView
                        ? $"Explained the Street View place in {review.Rounds[0].PlaceName}."
                        : $"Reviewed {review.Rounds.Count} round(s) of {review.Subject.Describe()} — {review.Missed} with the wrong country.",
                ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "The review could not be written just now.");

    private static ValueTask StopReportingAsync(AiProgressReporter? reporter) =>
        reporter?.DisposeAsync() ?? ValueTask.CompletedTask;

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("gaps", "Which questions the guides answer badly, by country and topic")]
    public Task GapsAsync(
        [Summary(description: "Days to cover; the configured period if left out")] int? days = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new ReadKnowledgeGapReportQuery(days), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var text = KnowledgeGapReportText.Render(result.Value);
            await FollowupAsync(text.Length <= 2000 ? text : $"{text[..1990]}\n…", ephemeral: true, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to build the knowledge-gap report.");

    [SlashCommand("feedback", "How people have rated my answers: totals, by model, and recent comments")]
    [HelpShownWhen(HelpToggle.AiFeedback)]
    public Task FeedbackAsync(
        [Summary(description: "Days to count over; 0 counts everything")] int days = 30) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new ReadAiFeedbackSummaryQuery(days), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: AiFeedbackFormatter.BuildSummaryEmbed(result.Value, days), ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to read the feedback summary.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("feedback-export", "Download the rated conversations as a JSONL file, to work out what to improve")]
    [HelpShownWhen(HelpToggle.AiFeedback)]
    public Task FeedbackExportAsync(
        [Summary(description: "Only good or only bad answers")]
        [Choice("good", "positive")] [Choice("bad", "negative")] string? rating = null,
        [Summary(description: "Days to export; 0 exports everything")] int days = 90) =>
        ExecuteAsync(async ct =>
        {
            AiFeedbackRating? wanted = rating switch
            {
                "positive" => AiFeedbackRating.Positive,
                "negative" => AiFeedbackRating.Negative,
                _ => null
            };

            var result = await Mediator.Send(new ExportAiFeedbackQuery(wanted, days, Limit: null), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            if (result.Value.Count == 0)
            {
                await FollowupAsync("Nothing has been rated in that window yet.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            // Written to a stream rather than a message: a single archived conversation already
            // exceeds Discord's 2000-character message limit.
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(AiFeedbackFormatter.RenderJsonLines(result.Value)));

            var fileName = $"ai-feedback-{DateOnly.FromDateTime(DateTime.UtcNow):yyyyMMdd}.jsonl";

            await FollowupWithFileAsync(stream, fileName,
                    text: $"**{result.Value.Count}** rated conversation(s), newest first.",
                    ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to export the feedback archive.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("sync-sources", "Refresh the catalogue of known guide sources")]
    [HelpDetail("Indexing runs by itself overnight; this and `/ai ingest` only kick it along.")]
    public Task SyncSourcesAsync() =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new SyncSourceCatalogsCommand(), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;
            await FollowupAsync(
                    $"Catalogue synced: **{report.Discovered}** listed, **{report.Added}** new, " +
                    $"**{report.Updated}** refreshed, **{report.Tombstoned}** no longer listed.",
                    ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to sync the source catalogue.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("ingest", "Index a batch of due guide sources now")]
    public Task IngestAsync(
        [Summary(description: "How many sources to process")] int count = 5,
        [Summary(description: "Only one source type, e.g. plonkit")] string? sourceType = null,
        [Summary(description: "Re-index even if the content is unchanged")] bool force = false) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new IngestKnowledgeSourcesCommand(count, sourceType, force), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;

            // A run that attempts nothing is the common first-time case, and four zeros explain
            // none of it: the queue is empty because nothing has been catalogued, or because
            // everything catalogued was indexed recently and is not due again yet.
            if (report is { Attempted: 0, BudgetExhausted: false, RateLimited: false, HostRefused: 0, Removed: 0 })
            {
                await FollowupAsync(await DescribeEmptyRunAsync(ct).ConfigureAwait(false), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            await FollowupAsync(AiSourcesFormatter.DescribeRun(report), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to index guide sources.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("backfill-images", "Queue indexed guides whose images were never embedded")]
    public Task BackfillImagesAsync() =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new BackfillMissingImagesCommand(), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;
            var message = report.SourcesWithMissingImages == 0
                ? "Every indexed image has been embedded — nothing to backfill."
                : $"Queued **{report.Queued}** of **{report.SourcesWithMissingImages}** source(s) with missing "
                  + "images. The nightly runs pick them up, or `/ai ingest` does sooner.";

            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to queue the image backfill.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("describe-pictures", "Have a vision model describe indexed guide pictures that have no words of their own")]
    public Task DescribePicturesAsync(
        [Summary(description: "How many pictures to describe")] int count = 10) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new CaptionPicturesCommand(count), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(AiSourcesFormatter.DescribeCaptionRun(result.Value), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to describe guide pictures.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("clue-now", "Post a clue of the day now, here or in the configured channel")]
    public Task ClueNowAsync(
        [Summary(description: "Post it in this channel instead of the configured one")] bool here = false) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new PostClueOfTheDayCommand(here ? Context.Channel.Id : null), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync($"Posted a clue from <{result.Value.SourceUrl}>.", ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to post a clue of the day.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("sources", "Guide sources that failed, were skipped or are unavailable, and why")]
    [HelpDetail("With how many times in a row it happened and when each is tried next.")]
    public Task SourcesAsync(
        [Summary(description: "Which sources to list")]
        [Choice("needing a look", nameof(KnowledgeSourceListKind.Problems))]
        [Choice("failed", nameof(KnowledgeSourceListKind.Failed))]
        [Choice("skipped", nameof(KnowledgeSourceListKind.Skipped))]
        [Choice("unavailable", nameof(KnowledgeSourceListKind.Unavailable))]
        [Choice("pending", nameof(KnowledgeSourceListKind.Pending))]
        [Choice("no longer listed", nameof(KnowledgeSourceListKind.Removed))]
        string status = nameof(KnowledgeSourceListKind.Problems),
        [Summary(description: "Only one source type, e.g. gdoc")] string? sourceType = null) =>
        ExecuteAsync(async ct =>
        {
            var kind = Enum.TryParse<KnowledgeSourceListKind>(status, out var parsed)
                ? parsed
                : KnowledgeSourceListKind.Problems;

            var result = await Mediator.Send(new ListKnowledgeSourcesQuery(kind, sourceType), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: AiSourcesFormatter.BuildListEmbed(result.Value, kind, sourceType), ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to list the guide sources.");

    [RequireUserPermission(GuildPermission.Administrator)]
    [SlashCommand("source-retry", "Queue failed, skipped or unavailable guide sources to be tried again")]
    [HelpDetail("E.g. after a site's extractor was fixed or a document was shared again. Give `key` or `source-type`.")]
    public Task SourceRetryAsync(
        [Summary(description: "One source, by the key or link /ai sources shows")] string? key = null,
        [Summary(description: "Every such source of one type, e.g. rmrg")] string? sourceType = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new RetryKnowledgeSourcesCommand(key, sourceType), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;
            var message = report.Queued == 0
                ? $"None of the **{report.Matched}** matching source(s) was failed, skipped or unavailable — nothing to queue."
                : $"Queued **{report.Queued}** of **{report.Matched}** matching source(s). The coming runs pick them "
                  + "up; `/ai ingest` does it now.";

            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to queue the guide sources.");

    /// <summary>
    /// Explains a run that indexed nothing. Which of the two reasons applies is the difference
    /// between "you have not started yet" and "there is nothing left to do", and the operator cannot
    /// tell them apart from the counts alone.
    /// </summary>
    private async Task<string> DescribeEmptyRunAsync(CancellationToken cancellationToken)
    {
        var sources = await Mediator.Send(new ReadKnowledgeSourceStatusQuery(), cancellationToken)
            .ConfigureAwait(false);

        if (sources.IsFailure || sources.Value.Total == 0)
        {
            return "Nothing to index yet — no guide sources are catalogued.\n"
                   + "Run `/ai sync-sources` first to discover them.";
        }

        return $"Nothing was due for indexing. Sources: {AiSourcesFormatter.DescribeCounts(sources.Value)}.\n"
               + "Already-indexed sources are left alone until their re-index interval passes; "
               + "use `force: true` to re-index them now. Failed, skipped and unavailable ones are listed "
               + "by `/ai sources` and queued again by `/ai source-retry`.";
    }

    /// <summary>Compact so several hits fit one ephemeral reply and can be judged at a glance.</summary>
    private static string FormatHits(IReadOnlyList<KnowledgeHit> hits)
    {
        var builder = new StringBuilder();

        foreach (var hit in hits)
        {
            var kind = hit.Kind == KnowledgeChunkKind.Image ? "🖼️" : "📄";
            var snippet = hit.Text.Length > 160 ? hit.Text[..160] + "…" : hit.Text;

            builder
                .AppendLine($"{kind} **{hit.Score:F3}** · {hit.SectionPath ?? hit.Title ?? "(untitled)"}")
                .AppendLine($"{snippet.ReplaceLineEndings(" ")}")
                .AppendLine($"<{hit.SourceUrl}>")
                .AppendLine();
        }

        return builder.ToString();
    }

    private async Task<string> ReadIndexSizeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var count = await _knowledgeIndex!.CountAsync(cancellationToken).ConfigureAwait(false);
            return count.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            // The vector store being unreachable is itself useful status, not a reason to fail the
            // whole command.
            return "unavailable";
        }
    }

    // Resolved optionally so the module still loads when the AI feature is switched off.
    private readonly IChatModelCatalog? _catalog = serviceProvider.GetService<IChatModelCatalog>();

    private readonly IKnowledgeIndex? _knowledgeIndex = serviceProvider.GetService<IKnowledgeIndex>();

    private readonly IImageRelay? _imageRelay = serviceProvider.GetService<IImageRelay>();
}
