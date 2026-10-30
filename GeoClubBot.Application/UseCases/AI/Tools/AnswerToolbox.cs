using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// What became of one tool call. Written by name wherever it is stored or exported, so the feedback
/// archive reads "NothingNew" rather than a number whose meaning depends on this order.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiToolCallOutcome>))]
public enum AiToolCallOutcome
{
    /// <summary>It ran and offered something new.</summary>
    Answered = 0,

    /// <summary>It ran, and found nothing, or only what had been offered already.</summary>
    NothingNew,

    /// <summary>Its arguments could not be read, or named something that does not exist; the model was told why.</summary>
    InvalidArguments,

    /// <summary>
    /// What it needed could not be reached, the day's allowance could not cover it, or no more excerpts
    /// could be numbered (<see cref="ExcerptLedger.MaxMarker"/>).
    /// </summary>
    Unavailable,

    /// <summary>The round already ran as many calls as it may.</summary>
    NotRun,

    /// <summary>
    /// A search ran and offered something new, but none of it from the country it named, and none from a
    /// guide filed under no country: the model was told so, and that if it needs that country, the
    /// guides seem to have nothing more on it.
    /// </summary>
    OffTarget
}

/// <summary>One tool call an answer made, for the logs, the retrieval probe and the feedback archive.</summary>
/// <param name="Round">Which round it was made in, from one.</param>
/// <param name="Arguments">Its arguments, shortened.</param>
/// <param name="NewExcerpts">How many excerpts it offered that had not been offered before.</param>
/// <param name="ResultCharacters">Length of what the model was shown.</param>
public sealed record AiToolCallSummary(
    int Round,
    string Tool,
    string Arguments,
    AiToolCallOutcome Outcome,
    int NewExcerpts,
    int ResultCharacters,
    long Milliseconds);

/// <summary>One call of a round, its arguments read.</summary>
/// <param name="Call">The call, with an id even when the model gave it none.</param>
/// <param name="Problem">What the model is told instead of a result, when the call cannot run.</param>
public sealed record PlannedToolCall(
    AiToolCall Call,
    SearchGuidesArguments? Search,
    ReadGuideArguments? Read,
    SpeciesRangeArguments? Species,
    string? Problem,
    AiToolCallOutcome ProblemOutcome);

/// <summary>A round's calls before they run: what they ask for, and what that costs.</summary>
public sealed record ToolRoundPlan(int Round, IReadOnlyList<PlannedToolCall> Calls)
{
    /// <summary>The round searches the guides, so it needs its one embedding request.</summary>
    public bool Searches => Calls.Any(call => call.Search is not null);
}

/// <param name="Calls">The round's calls, in order, as the assistant message has to repeat them.</param>
/// <param name="Results">What each call returned, in the same order.</param>
public sealed record ToolRoundResult(
    IReadOnlyList<AiToolCall> Calls,
    IReadOnlyList<string> Results,
    IReadOnlyList<AiToolCallSummary> Summaries);

/// <summary>
/// Runs the answer's tool calls, a round at a time.
///
/// A round's calls run together, and its searches share one embedding request: a model comparing two
/// countries asks for both at once (nemotron-3.5-super-vl did, 2026-10-23), and two searches should
/// cost what one does. Whatever a call finds is offered through the <see cref="ExcerptLedger"/>, in
/// the order the model made the calls, so the numbering does not depend on which finished first.
///
/// Nothing here throws to the caller or spends the daily allowance: the answer's loop reserves the
/// round's embedding before asking for it, and every failure becomes a short message the model is
/// shown in place of the result, so it can still answer with what it has.
///
/// A search that comes back empty-handed says so in as many words. In the answer replay of 2026-10-29
/// models kept searching for what the library lacks: every answer that took three rounds searched once
/// a round, the third search of a Philippine rice question came back with Thai excerpts, and those
/// answers grew to 25–29 excerpts and 7–8 requests without citing one excerpt the third round found. So
/// a search that offered nothing new tells the model the guides seem not to cover more of it, and one
/// that offered nothing from the country it was about — named as its country, or in its words — says
/// so first, and that if it is that country the model needs, it should answer with what it has.
///
/// Only the search's own country counts, never the question's. A search giving none looks across
/// countries on purpose: 6 of the replay's 37 searches gave none, and each was after several — every
/// country with Gen 2, the Balkans beside Kosovo, Scots pine in Europe beside the Pacific Northwest —
/// and the Scots pine one found the Spanish excerpt its answer cited. Judged by the question's one
/// country, a comparison ("which other countries use the same bollards as Poland?") would be told that
/// the excerpts answering it are off target. And an excerpt from a guide filed under no country, a
/// multi-country guide of the library, may well cover the country searched, so with one among the new
/// excerpts nothing is said.
/// </summary>
public sealed partial class AnswerToolbox(
    IEmbedder embedder,
    IKnowledgeIndex knowledgeIndex,
    ISpeciesRangeReader speciesRange,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiRetrievalConfiguration> retrievalConfiguration,
    IOptions<AiToolsConfiguration> toolsConfiguration,
    ILogger<AnswerToolbox> logger)
{
    /// <summary>New excerpts one search offers at most, beside the ten the question came with.</summary>
    public const int MaxSearchExcerpts = 6;

    /// <summary>Chunks one read of a guide offers at most.</summary>
    public const int MaxGuideChunks = 8;

    /// <summary>Text one read of a guide offers at most: about four chunks' worth, so a read is never the bulk of the prompt.</summary>
    public const int MaxGuideCharacters = 6_000;

    /// <summary>A guide's chunks read back to choose from. The largest guides hold a few hundred.</summary>
    private const int MaxGuideChunksRead = 1_000;

    /// <summary>Candidates one search asks the index for: enough to find six new ones past those offered.</summary>
    private const int MaxSearchCandidates = 40;

    /// <summary>Reads a round's calls, before anything runs or is paid for.</summary>
    public ToolRoundPlan Plan(IReadOnlyList<AiToolCall> calls, int round, ExcerptLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(ledger);

        var settings = toolsConfiguration.Value;
        var planned = new List<PlannedToolCall>();

        for (var index = 0; index < calls.Count; index++)
        {
            // The result has to name the call it answers, and a provider that sent no id gets one.
            var call = string.IsNullOrWhiteSpace(calls[index].Id)
                ? calls[index] with { Id = $"call_{round.ToString(CultureInfo.InvariantCulture)}_{index.ToString(CultureInfo.InvariantCulture)}" }
                : calls[index];

            if (index >= settings.MaxToolCallsPerRound)
            {
                planned.Add(Refused(call,
                    $"Not run: at most {settings.MaxToolCallsPerRound} tool calls are run at a time. Answer with what you have, or ask again.",
                    AiToolCallOutcome.NotRun));
                continue;
            }

            planned.Add(call.Name switch
            {
                AnswerTools.SearchGuides => ToolArguments.TryReadSearchGuides(call.ArgumentsJson, out var search, out var problem)
                    ? new PlannedToolCall(call, search, null, null, null, default)
                    : Refused(call, problem, AiToolCallOutcome.InvalidArguments),
                AnswerTools.ReadGuide => ToolArguments.TryReadReadGuide(call.ArgumentsJson, out var read, out var problem)
                    ? ledger.HitAt(read.Excerpt) is null
                        ? Refused(call, $"There is no excerpt [{read.Excerpt}]; the excerpts run from [1] to [{ledger.Count}].",
                            AiToolCallOutcome.InvalidArguments)
                        : new PlannedToolCall(call, null, read, null, null, default)
                    : Refused(call, problem, AiToolCallOutcome.InvalidArguments),
                AnswerTools.SpeciesRange when settings.SpeciesRange.Enabled =>
                    ToolArguments.TryReadSpeciesRange(call.ArgumentsJson, out var species, out var problem)
                        ? new PlannedToolCall(call, null, null, species, null, default)
                        : Refused(call, problem, AiToolCallOutcome.InvalidArguments),
                _ => Refused(call,
                    $"There is no tool called '{call.Name}'. The tools are {string.Join(", ", AnswerTools.Definitions(settings.SpeciesRange.Enabled).Select(tool => tool.Name))}.",
                    AiToolCallOutcome.InvalidArguments)
            });
        }

        return new ToolRoundPlan(round, planned);
    }

    /// <summary>Runs a planned round and offers what it finds.</summary>
    /// <param name="searchAllowed">
    /// Whether the round's embedding request was reserved. When it was not, its searches are answered
    /// "unavailable" and the rest still run, since they cost no request.
    /// </param>
    public async Task<ToolRoundResult> RunAsync(
        ToolRoundPlan plan,
        ExcerptLedger ledger,
        bool searchAllowed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);

        var started = Stopwatch.GetTimestamp();

        // Read once for the round: searches and species lookups both resolve countries with it. Never
        // fails; an unread list only means no country is recognised.
        var gazetteer = plan.Calls.Any(call => call.Search is not null || call.Species?.Country is not null)
            ? await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false)
            : CountryGazetteer.Empty;

        var vectors = searchAllowed && plan.Searches
            ? await EmbedSearchesAsync(plan, cancellationToken).ConfigureAwait(false)
            : null;

        var searchIndex = 0;
        var work = new List<Task<CallData>>();
        foreach (var call in plan.Calls)
        {
            work.Add(call switch
            {
                { Problem: { } problem } => Task.FromResult<CallData>(new CallData.Refused(problem, call.ProblemOutcome)),
                { Search: { } search } => SearchAsync(search, vectors, searchIndex++, searchAllowed, gazetteer, ledger, cancellationToken),
                { Read: { } read } => ReadGuideAsync(read, ledger, cancellationToken),
                { Species: { } species } => ReadSpeciesAsync(species, gazetteer, cancellationToken),
                _ => Task.FromResult<CallData>(new CallData.Refused("Not run.", AiToolCallOutcome.NotRun))
            });
        }

        var data = await Task.WhenAll(work).ConfigureAwait(false);

        var results = new List<string>();
        var summaries = new List<AiToolCallSummary>();
        for (var index = 0; index < plan.Calls.Count; index++)
        {
            var (result, outcome, offered) = Offer(data[index], ledger, plan.Calls[index]);
            var summary = new AiToolCallSummary(
                plan.Round,
                plan.Calls[index].Call.Name,
                ToolArguments.Shorten(plan.Calls[index].Call.ArgumentsJson),
                outcome,
                offered,
                result.Length,
                (long)Stopwatch.GetElapsedTime(started, data[index].FinishedAt).TotalMilliseconds);

            LogToolCall(logger, summary.Round, summary.Tool, summary.Arguments, summary.Outcome, summary.NewExcerpts,
                summary.ResultCharacters, summary.Milliseconds);

            results.Add(result);
            summaries.Add(summary);
        }

        return new ToolRoundResult([.. plan.Calls.Select(call => call.Call)], results, summaries);
    }

    private static PlannedToolCall Refused(AiToolCall call, string problem, AiToolCallOutcome outcome) =>
        new(call, null, null, null, problem, outcome);

    /// <summary>Every search of the round in one embedding request, or null when that request failed.</summary>
    private async Task<IReadOnlyList<ReadOnlyMemory<float>>?> EmbedSearchesAsync(
        ToolRoundPlan plan,
        CancellationToken cancellationToken)
    {
        var queries = plan.Calls.Where(call => call.Search is not null).Select(call => call.Search!.Query).ToList();

        try
        {
            var embedded = await embedder
                .EmbedAsync([.. queries.Select(query => new TextEmbeddingInput(query))], cancellationToken)
                .ConfigureAwait(false);

            if (embedded.IsSuccess && embedded.Value is { } vectors && vectors.Count == queries.Count)
            {
                return vectors;
            }

            LogSearchEmbeddingFailed(logger, embedded.IsFailure ? embedded.Error.Code ?? "(none)" : "wrong vector count");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogSearchEmbeddingThrew(logger, exception);
            return null;
        }
    }

    /// <summary>
    /// The bot's own retrieval, run for the model's wording: the same lists, the same fusion, the same
    /// country boost — a named country is preferred, never required, so "Scandinavia" or a neighbour
    /// the model forgot can still come back.
    /// </summary>
    private async Task<CallData> SearchAsync(
        SearchGuidesArguments search,
        IReadOnlyList<ReadOnlyMemory<float>>? vectors,
        int index,
        bool searchAllowed,
        CountryGazetteer gazetteer,
        ExcerptLedger ledger,
        CancellationToken cancellationToken)
    {
        if (!searchAllowed)
        {
            return CallData.Unavailable(
                "The guide search is not available just now: today's AI request allowance is used up. Answer with what you have.");
        }

        if (vectors is null)
        {
            return CallData.Unavailable("The guide search failed just now. Answer with what you have.");
        }

        try
        {
            var settings = retrievalConfiguration.Value;
            var composed = RetrievalQueryComposer.Compose(search.Query, [], hasScreenshot: false, plan: null, gazetteer, settings);

            var named = search.Country is null ? [] : gazetteer.Resolve(search.Country);
            var countries = composed.Countries.Concat(named).Distinct().ToList();

            var query = composed.ToQuery([vectors[index]], imageVector: null, settings) with
            {
                BoostCountries = countries.Count <= settings.CountryBoost.MaxCountries
                    ? [.. countries.SelectMany(country => country.IndexNames).Distinct(StringComparer.Ordinal)]
                    : [],
                ChunkKind = search.PicturesOnly ? KnowledgeChunkKind.Image : null,
                // Enough to find new ones past every excerpt already offered.
                Limit = Math.Min(MaxSearchCandidates, ledger.Count + MaxSearchExcerpts)
            };

            var hits = await knowledgeIndex.SearchAsync(query, cancellationToken).ConfigureAwait(false) ?? [];
            var note = search.Country is not null && named.Count == 0
                ? $"No guides are filed under '{search.Country}', so every country was searched."
                : null;

            // What the search is about: the country it names, else those its words name; none when it
            // names none, since it then looks across countries on purpose.
            IReadOnlyList<CountryMention> about = named.Count > 0 ? named : composed.Countries;
            var aboutName = named.Count > 0 ? search.Country
                : about.Count > 0 ? string.Join(" or ", about.Select(country => TitleCase(country.Name)))
                : null;

            return new CallData.Found([.. hits], MaxSearchExcerpts, note,
                hits.Count == 0 ? "Nothing in the guides matched this search." : null)
            {
                Searched = new SearchedFor(
                    [.. about.SelectMany(country => country.IndexNames).Distinct(StringComparer.OrdinalIgnoreCase)], aboutName)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogToolThrew(logger, AnswerTools.SearchGuides, exception);
            return CallData.Unavailable("The guide index could not be reached just now. Answer with what you have.");
        }
    }

    private async Task<CallData> ReadGuideAsync(ReadGuideArguments read, ExcerptLedger ledger, CancellationToken cancellationToken)
    {
        var anchor = ledger.HitAt(read.Excerpt)!;
        if (string.IsNullOrWhiteSpace(anchor.SourceType) || string.IsNullOrWhiteSpace(anchor.SourceKey))
        {
            return new CallData.Refused(
                $"Excerpt [{read.Excerpt}] is not from a guide that can be read further.", AiToolCallOutcome.InvalidArguments);
        }

        try
        {
            var chunks = await knowledgeIndex
                .ReadSourceChunksAsync(anchor.SourceType, anchor.SourceKey, MaxGuideChunksRead, cancellationToken)
                .ConfigureAwait(false) ?? [];

            var chosen = GuideReader.SelectAround(anchor, [.. chunks], ledger, MaxGuideChunks, MaxGuideCharacters);
            return new CallData.Found(chosen, MaxGuideChunks, Note: null,
                chosen.Count == 0 ? $"Nothing more: the rest of the guide behind [{read.Excerpt}] has been offered already, or holds nothing else." : null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogToolThrew(logger, AnswerTools.ReadGuide, exception);
            return CallData.Unavailable("The guide index could not be reached just now. Answer with what you have.");
        }
    }

    private async Task<CallData> ReadSpeciesAsync(
        SpeciesRangeArguments species,
        CountryGazetteer gazetteer,
        CancellationToken cancellationToken)
    {
        string? code = null;
        if (species.Country is not null)
        {
            code = IsoCountries.Resolve(species.Country, gazetteer);
            if (code is null)
            {
                return new CallData.Refused(
                    $"'{species.Country}' is not one country. Name a single country, or leave country out to count every country.",
                    AiToolCallOutcome.InvalidArguments);
            }
        }

        try
        {
            var range = await speciesRange
                .ReadAsync(new SpeciesRangeQuery(species.Name, code), cancellationToken)
                .ConfigureAwait(false);

            if (range.IsSuccess && range.Value is { } found)
            {
                return new CallData.Found(
                    [SpeciesRangeExcerpt.ToHit(found, species.Name, toolsConfiguration.Value.SpeciesRange.MaxAreas)], 1, null, null);
            }

            return range.Error.Type == ErrorType.NotFound && range.Error.Code is not null
                ? new CallData.Refused($"GBIF knows no plant called '{species.Name}'. Try its scientific name.", AiToolCallOutcome.NothingNew)
                : CallData.Unavailable("The species database could not be reached just now. Answer with what you have.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogToolThrew(logger, AnswerTools.SpeciesRange, exception);
            return CallData.Unavailable("The species database could not be reached just now. Answer with what you have.");
        }
    }

    /// <summary>What a search that found nothing new is told, after saying so: stop looking, and say what is missing.</summary>
    private const string NothingToSearchFor =
        "The guides seem not to cover more of this than you have. Answer with what is there, and say plainly what they don't cover.";

    /// <summary>What a search that found nothing from its country is told first: if that country is what it needs, stop looking.</summary>
    private static string OffTargetNote(string country) =>
        $"None of these new excerpts is from {country}. If it is {country} you need, the guides seem to have nothing more "
        + "on it than you have: answer with what you have.\n\n";

    /// <summary>What the model is told once no more excerpts can be numbered (<see cref="ExcerptLedger.IsFull"/>).</summary>
    private static readonly string ExcerptsFull =
        $"No more excerpts can be offered: the numbers up to [{ExcerptLedger.MaxMarker.ToString(CultureInfo.InvariantCulture)}] "
        + "are all taken. Answer with the excerpts you have.";

    /// <summary>Offers what a call found, in call order, and says what the model is shown.</summary>
    private static (string Result, AiToolCallOutcome Outcome, int Offered) Offer(
        CallData data,
        ExcerptLedger ledger,
        PlannedToolCall call)
    {
        switch (data)
        {
            case CallData.Refused refused:
                return (refused.Message, refused.Outcome, 0);
            case CallData.Found found:
                {
                    var text = new StringBuilder();
                    if (found.Note is not null)
                    {
                        text.AppendLine(found.Note).AppendLine();
                    }

                    var offered = 0;
                    var full = false;
                    var fromCountry = false;
                    var unfiled = false;
                    foreach (var hit in found.Hits)
                    {
                        if (offered >= found.MaxNew)
                        {
                            break;
                        }

                        if (ledger.IsFull)
                        {
                            full = !ledger.Holds(hit);
                            if (full)
                            {
                                break;
                            }

                            continue;
                        }

                        if (ledger.Offer(hit) is { } excerpt)
                        {
                            text.AppendLine(excerpt).AppendLine();
                            offered++;
                            fromCountry |= found.Searched?.IsFrom(hit) == true;
                            unfiled |= string.IsNullOrWhiteSpace(hit.Country);
                        }
                    }

                    if (full)
                    {
                        // Said rather than passed off as nothing new: the guides did have more to say.
                        text.Append(ExcerptsFull);
                        return (text.ToString().Trim(), offered == 0 ? AiToolCallOutcome.Unavailable : AiToolCallOutcome.Answered, offered);
                    }

                    if (offered == 0)
                    {
                        text.Append(found.WhenEmpty
                                    ?? (call.Read is not null
                                        ? "Nothing more: everything near that excerpt has been offered already."
                                        : "Nothing new: everything this found is among the numbered excerpts already."));
                        if (call.Search is not null)
                        {
                            text.Append(' ').Append(NothingToSearchFor);
                        }

                        return (text.ToString().Trim(), AiToolCallOutcome.NothingNew, 0);
                    }

                    if (found.Searched is { Name: { } country } && !fromCountry && !unfiled)
                    {
                        // Said first, where a model reading the result cannot miss it; the excerpts are
                        // still offered, for a look-alike worth ruling out. Said as a condition, not as a
                        // verdict: a search can name a country to find others like it.
                        text.Insert(0, OffTargetNote(country));
                        return (text.ToString().Trim(), AiToolCallOutcome.OffTarget, offered);
                    }

                    return (text.ToString().Trim(), AiToolCallOutcome.Answered, offered);
                }
            default:
                return ("Not run.", AiToolCallOutcome.NotRun, 0);
        }
    }

    /// <summary>What a call came back with, before it is offered.</summary>
    private abstract record CallData
    {
        public long FinishedAt { get; } = Stopwatch.GetTimestamp();

        public static CallData Unavailable(string message) => new Refused(message, AiToolCallOutcome.Unavailable);

        /// <param name="MaxNew">Most of <paramref name="Hits"/> to offer.</param>
        /// <param name="Note">Said before the excerpts.</param>
        /// <param name="WhenEmpty">Said when nothing was found at all.</param>
        public sealed record Found(IReadOnlyList<KnowledgeHit> Hits, int MaxNew, string? Note, string? WhenEmpty) : CallData
        {
            /// <summary>For a search, the country it was about; null for any other call.</summary>
            public SearchedFor? Searched { get; init; }
        }

        public sealed record Refused(string Message, AiToolCallOutcome Outcome) : CallData;
    }

    /// <summary>A search's country, as the index files it and as the model is told of it.</summary>
    /// <param name="IndexNames">Every spelling the index files it under; empty when the search was about no country.</param>
    /// <param name="Name">How the model is told of it; null when the search was about none.</param>
    private sealed record SearchedFor(IReadOnlyList<string> IndexNames, string? Name)
    {
        public bool IsFrom(KnowledgeHit hit) =>
            hit.Country is { } country && IndexNames.Contains(country, StringComparer.OrdinalIgnoreCase);
    }

    private static string TitleCase(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);

    [LoggerMessage(LogLevel.Information,
        "AI tool call (round {Round}) {Tool} {Arguments}: {Outcome}, {NewExcerpts} new excerpt(s), {Characters} characters, {Milliseconds} ms.")]
    static partial void LogToolCall(
        ILogger logger, int round, string tool, string arguments, AiToolCallOutcome outcome, int newExcerpts, int characters, long milliseconds);

    [LoggerMessage(LogLevel.Warning, "Embedding the AI tool searches failed ({ErrorCode}); they were answered as unavailable.")]
    static partial void LogSearchEmbeddingFailed(ILogger logger, string errorCode);

    [LoggerMessage(LogLevel.Warning, "Embedding the AI tool searches failed unexpectedly; they were answered as unavailable.")]
    static partial void LogSearchEmbeddingThrew(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "AI tool {Tool} failed unexpectedly; the model was told it is unavailable.")]
    static partial void LogToolThrew(ILogger logger, string tool, Exception exception);
}
