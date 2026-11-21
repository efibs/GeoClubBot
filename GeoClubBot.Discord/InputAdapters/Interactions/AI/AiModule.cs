using System.Globalization;
using System.Text;
using Configuration;
using Constants;
using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
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
[Group("ai", "Commands for controlling the AI features")]
public partial class AiModule(IServiceProvider serviceProvider, ISender mediator, ILogger<AiModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    /// <summary>Hits shown by /ai search; enough to judge retrieval without flooding the reply.</summary>
    private const int SearchResultLimit = 5;

    [SlashCommand("status", "Show which AI models are available, how much is indexed, and today's budget")]
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

    [SlashCommand("search", "Show what the guide index returns for a query, without asking a model")]
    public Task SearchAsync(
        [Summary(description: "What to look for")] string query,
        [Summary(description: "Restrict to one country")] string? country = null) =>
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("quiz", "A guide picture with four answers to pick from; everyone guesses once and scores points")]
    public Task QuizAsync(
        [Summary(description: "Only pictures of one country, e.g. Vietnam: then it asks where in it, or what is typical")] string? country = null,
        [Summary(description: "How hard; left out, easy and medium questions")]
        [Choice("easy (1 point)", "easy")]
        [Choice("medium (2 points)", "medium")]
        [Choice("hard (3 points)", "hard")]
        string? difficulty = null,
        [Summary(name: "lesser-known", description: "Only clues PlonkIt does not have")] bool lesserKnown = false) =>
        ExecuteAsync(ct => PostQuizAsync(country, lesserKnown, ParseDifficulty(difficulty), ct),
            failureMessage: "The quiz could not draw a picture just now.");

    [SlashCommand("quiz-scores", "The quiz leaderboard: points over all time and over the last days")]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("gaps", "Show which questions the guides answer badly, by country and topic")]
    public Task GapsAsync(
        [Summary(description: "Days to cover; leave empty for the configured period")] int? days = null) =>
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

    [SlashCommand("feedback", "Show how people have rated my answers")]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("feedback-export", "Download the rated conversations as JSONL")]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("sync-sources", "Refresh the catalogue of known guide sources")]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("ingest", "Index a batch of due guide sources now")]
    public Task IngestAsync(
        [Summary(description: "How many sources to process")] int count = 5,
        [Summary(description: "Restrict to one source type, e.g. plonkit")] string? sourceType = null,
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("sources", "List guide sources that failed, were skipped or are unavailable, and why")]
    public Task SourcesAsync(
        [Summary(description: "Which sources to list")]
        [Choice("needing a look", nameof(KnowledgeSourceListKind.Problems))]
        [Choice("failed", nameof(KnowledgeSourceListKind.Failed))]
        [Choice("skipped", nameof(KnowledgeSourceListKind.Skipped))]
        [Choice("unavailable", nameof(KnowledgeSourceListKind.Unavailable))]
        [Choice("pending", nameof(KnowledgeSourceListKind.Pending))]
        [Choice("no longer listed", nameof(KnowledgeSourceListKind.Removed))]
        string status = nameof(KnowledgeSourceListKind.Problems),
        [Summary(description: "Restrict to one source type, e.g. gdoc")] string? sourceType = null) =>
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

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("source-retry", "Queue failed, skipped or unavailable guide sources to be tried again")]
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
