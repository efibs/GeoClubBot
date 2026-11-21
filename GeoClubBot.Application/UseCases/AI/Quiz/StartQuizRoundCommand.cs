using System.Globalization;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Utilities;

namespace UseCases.UseCases.AI.Quiz;

/// <summary>Draws a quiz question for a channel and records it as a round, before it is posted (#491).</summary>
/// <param name="Country">A country to draw from, as the member wrote it; null draws from every country.</param>
/// <param name="LesserKnownOnly">Leave out PlonkIt's pictures and the clues PlonkIt has a tip like.</param>
/// <param name="Difficulty">The difficulty asked for; null draws easy and medium questions.</param>
public sealed record StartQuizRoundCommand(
    ulong ChannelId,
    ulong UserId,
    string? Country = null,
    bool LesserKnownOnly = false,
    AiQuizDifficulty? Difficulty = null) : ICommand<Result<AiQuizRound>>;

public static class QuizErrorCodes
{
    public const string Disabled = "ai.quiz_disabled";
    public const string UnknownCountry = "ai.quiz_unknown_country";
    public const string NotReady = "ai.quiz_not_ready";
    public const string RoundNotFound = "ai.quiz_round_not_found";
    public const string AlreadyGuessed = "ai.quiz_already_guessed";
    public const string AlreadyRevealed = "ai.quiz_already_revealed";
    public const string NotYoursToReveal = "ai.quiz_not_yours_to_reveal";
}

/// <summary>
/// Picks a rated picture that makes the question wanted: "Where is this?" with no country chosen;
/// "Where in X?" for a country, else "What here is typical of X?" — asking which country a member has
/// just named is no question. Pictures never asked come first. When no rated picture fits, a few drawn
/// from the index are rated while the member waits, from the day's whole allowance.
/// </summary>
public sealed partial class StartQuizRoundHandler(
    IKnowledgeIndex knowledgeIndex,
    IAiQuizItemRepository items,
    IAiQuizRoundRepository rounds,
    QuizPictureRater rater,
    MainGuideOverlap mainGuide,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiQuizConfiguration> configuration,
    IOptions<AiConfiguration> aiConfiguration,
    TimeProvider timeProvider,
    ILogger<StartQuizRoundHandler> logger)
    : IRequestHandler<StartQuizRoundCommand, Result<AiQuizRound>>
{
    /// <summary>Rated pictures read per kind asked; a few may be gone from the index or changed since.</summary>
    private const int CandidatesPerDraw = 8;

    /// <summary>The longest explanation stored; the reveal cuts it shorter.</summary>
    private const int MaxExplanationLength = 1_000;

    public async Task<Result<AiQuizRound>> Handle(StartQuizRoundCommand request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;
        if (!aiConfiguration.Value.Active || !settings.Enabled)
        {
            return Error.Validation(QuizErrorCodes.Disabled, "The quiz is turned off.");
        }

        IReadOnlyList<string>? spellings = null;
        string? countryName = null;
        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);
            var mentions = gazetteer.Resolve(request.Country);
            if (mentions.Count != 1)
            {
                return Error.Validation(QuizErrorCodes.UnknownCountry,
                    mentions.Count == 0
                        ? $"No indexed guide covers '{request.Country.Trim()}'."
                        : $"'{request.Country.Trim()}' is several countries; name one.");
            }

            spellings = mentions[0].IndexNames;
            countryName = IsoCountries.Resolve(mentions[0].Name, gazetteer) is { } code
                ? IsoCountries.NameOf(code)
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mentions[0].Name);
        }

        var wanted = new QuizWanted(
            spellings is null ? [AiQuizQuestionKind.Country] : [AiQuizQuestionKind.Region, AiQuizQuestionKind.Clue],
            request.Difficulty is { } difficulty ? [difficulty] : [AiQuizDifficulty.Easy, AiQuizDifficulty.Medium]);
        var excluded = request.LesserKnownOnly ? MainGuideOverlap.MainGuide : null;

        var picked = await DrawRatedAsync(wanted, spellings, excluded, request.LesserKnownOnly, cancellationToken).ConfigureAwait(false)
                     ?? await RateOnTheSpotAsync(wanted, spellings, excluded, request.LesserKnownOnly, cancellationToken).ConfigureAwait(false);

        if (picked is not { } pick)
        {
            LogNotReady(logger, request.Country ?? "any country", request.Difficulty?.ToString() ?? "easy or medium", request.LesserKnownOnly);
            return Error.NotFound(QuizErrorCodes.NotReady,
                "No picture is ready for that choice yet. Pictures are rated for the quiz every night, so try again later"
                + (request.Difficulty is null && request.Country is null && !request.LesserKnownOnly ? "." : ", or with fewer filters."));
        }

        var round = ToRound(pick.Hit, pick.Question, countryName, request, settings, timeProvider.GetUtcNow());
        rounds.Add(round);
        LogStarted(logger, pick.Hit.SourceUrl, round.Kind, round.Difficulty, request.Country ?? "any country");
        return round;
    }

    /// <summary>The first rated picture that fits, kinds in the order wanted; null when none does.</summary>
    private async Task<(KnowledgeHit Hit, AiQuizItemQuestion Question)?> DrawRatedAsync(
        QuizWanted wanted,
        IReadOnlyList<string>? spellings,
        string? excluded,
        bool lesserKnownOnly,
        CancellationToken cancellationToken)
    {
        foreach (var kind in wanted.Kinds)
        {
            var drawn = await items.DrawAsync(
                    new QuizItemDraw(kind, wanted.Difficulties, rater.Version, spellings?.ToList(), excluded),
                    CandidatesPerDraw,
                    cancellationToken)
                .ConfigureAwait(false);
            if (drawn.Count == 0)
            {
                continue;
            }

            var hits = await knowledgeIndex.ReadChunksAsync([.. drawn.Select(item => item.ChunkId)], cancellationToken).ConfigureAwait(false);
            var byId = hits.ToDictionary(hit => hit.Id);

            // A picture gone from the index, or re-indexed with other words, is forgotten: the nightly run rates it anew.
            var stale = drawn
                .Where(item => !byId.TryGetValue(item.ChunkId, out var hit) || QuizPictureRater.ContentHashOf(hit) != item.ContentHash)
                .Select(item => item.ChunkId)
                .ToList();
            if (stale.Count > 0)
            {
                await items.RemoveAsync(stale, cancellationToken).ConfigureAwait(false);
            }

            var fresh = drawn.Where(item => !stale.Contains(item.ChunkId)).Select(item => byId[item.ChunkId]).ToList();
            fresh = await KeepLesserKnownAsync(fresh, lesserKnownOnly, cancellationToken).ConfigureAwait(false);

            foreach (var item in drawn)
            {
                var hit = fresh.FirstOrDefault(candidate => candidate.Id == item.ChunkId);
                if (hit is not null && item.QuestionOf(kind) is { } question)
                {
                    return (hit, question);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Rates a few unrated pictures drawn from the index, and asks the first that fits. Each rating is kept
    /// whether or not it fits, so no request is spent twice on one picture.
    /// </summary>
    private async Task<(KnowledgeHit Hit, AiQuizItemQuestion Question)?> RateOnTheSpotAsync(
        QuizWanted wanted,
        IReadOnlyList<string>? spellings,
        string? excluded,
        bool lesserKnownOnly,
        CancellationToken cancellationToken)
    {
        var attempts = configuration.Value.OnTheSpotRatings;
        if (attempts <= 0)
        {
            return null;
        }

        var drawn = new List<KnowledgeHit>();
        foreach (var country in spellings ?? [null!])
        {
            drawn.AddRange(await knowledgeIndex
                .SampleAsync(new KnowledgeSample(Math.Max(1, configuration.Value.Draws), KnowledgeChunkKind.Image, excluded, country), cancellationToken)
                .ConfigureAwait(false));
        }

        var candidates = drawn.Where(QuizPictureRater.IsRateable).DistinctBy(hit => hit.Id).ToList();
        var hashes = await items.ReadContentHashesAsync([.. candidates.Select(hit => hit.Id)], rater.Version, cancellationToken)
            .ConfigureAwait(false);
        candidates = [.. candidates.Where(hit => !hashes.TryGetValue(hit.Id, out var hash) || hash != QuizPictureRater.ContentHashOf(hit))];
        candidates = await KeepLesserKnownAsync(candidates, lesserKnownOnly, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return null;
        }

        var chain = await rater.ReadChainAsync(cancellationToken).ConfigureAwait(false);
        foreach (var picture in candidates.Take(attempts))
        {
            if (chain.Count == 0 || !await rater.TryReserveAsync(100, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var outcome = await rater.RateAsync(picture, chain, cancellationToken).ConfigureAwait(false);
            if (outcome.Kind == QuizRatingOutcomeKind.RateLimited)
            {
                return null;
            }

            if (outcome.Kind == QuizRatingOutcomeKind.Failed)
            {
                chain = await rater.ReadChainAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (outcome.Item is { } item)
            {
                foreach (var kind in wanted.Kinds)
                {
                    if (item.QuestionOf(kind) is { } question && wanted.Difficulties.Contains(question.Difficulty))
                    {
                        return (picture, question);
                    }
                }
            }
        }

        return null;
    }

    private async Task<List<KnowledgeHit>> KeepLesserKnownAsync(List<KnowledgeHit> hits, bool lesserKnownOnly, CancellationToken cancellationToken)
    {
        if (!lesserKnownOnly || hits.Count == 0)
        {
            return hits;
        }

        var marked = await mainGuide.MarkAsync(hits, cancellationToken).ConfigureAwait(false);
        var told = marked.Where(hit => hit.AlsoInMainGuide).Select(hit => hit.Id).ToHashSet();
        return [.. hits.Where(hit => !told.Contains(hit.Id))];
    }

    /// <summary>The round as posted: the options shuffled, the guide's words kept for the answer.</summary>
    internal static AiQuizRound ToRound(
        KnowledgeHit hit,
        AiQuizItemQuestion question,
        string? askedCountryName,
        StartQuizRoundCommand request,
        AiQuizConfiguration settings,
        DateTimeOffset nowUtc)
    {
        var options = question.Decoys.Take(AiQuizRound.OptionCount - 1).Append(question.Answer).ToArray();
        Random.Shared.Shuffle(options);

        var guide = string.IsNullOrWhiteSpace(hit.Title) ? "a community guide" : hit.Title.Trim();
        var section = SectionLabel.Clean(hit.SectionPath);
        if (section.StartsWith(guide, StringComparison.OrdinalIgnoreCase))
        {
            section = section[guide.Length..].TrimStart(' ', '>', '—').Trim();
        }

        var explanation = string.Join(' ', hit.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        if (explanation.Length > MaxExplanationLength)
        {
            explanation = $"{explanation[..MaxExplanationLength].TrimEnd()}…";
        }

        // "Where in Mongolia…": the country the question is within, the picture's own for a regional or
        // clue question even when the member spelt it otherwise.
        var countryName = question.Kind == AiQuizQuestionKind.Country
            ? null
            : QuizPictureRater.CountryCodeOf(hit) is { } code ? IsoCountries.NameOf(code) : askedCountryName;

        return AiQuizRound.Create(
            hit.Id,
            question.Kind,
            question.Difficulty,
            options,
            Array.IndexOf(options, question.Answer),
            countryName,
            guide,
            section,
            explanation,
            hit.SourceUrl,
            hit.ImageUrl!,
            request.Country?.Trim(),
            request.LesserKnownOnly,
            request.Difficulty,
            request.ChannelId,
            request.UserId,
            nowUtc,
            TimeSpan.FromMinutes(Math.Max(1, settings.RevealAfterMinutes)));
    }

    private sealed record QuizWanted(IReadOnlyList<AiQuizQuestionKind> Kinds, IReadOnlyList<AiQuizDifficulty> Difficulties);

    [LoggerMessage(LogLevel.Information, "Quiz: asking {SourceUrl} as a {Kind} question ({Difficulty}) for {Country}.")]
    static partial void LogStarted(ILogger logger, string sourceUrl, AiQuizQuestionKind kind, AiQuizDifficulty difficulty, string country);

    [LoggerMessage(LogLevel.Information, "Quiz: nothing ready for {Country}, {Difficulty} (lesser-known only: {LesserKnown}).")]
    static partial void LogNotReady(ILogger logger, string country, string difficulty, bool lesserKnown);
}
