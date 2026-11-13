using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Retrieval;

/// <summary>
/// Draws picture clues at random from the guides other than PlonkIt, for a question that asks for
/// something new and names nothing to look for (<see cref="LesserKnownQuestion.NamesNoSubject"/>):
/// "tell me a reliable clue I don't know yet".
///
/// A search can only go by such a question's wording, and what is worded like it is no clue. Asked
/// exactly that, the bot was offered a document's legend and answered "the guide maps each meta by
/// region, using dark red for where it's most common" — no clue and no picture, where the member had
/// asked for both (2026-11-09: "No images"; replayed 2026-11-11). Chance does better: a clue with its
/// picture from each of a handful of guides, and a different handful the next time, which is what
/// "another one" asks for.
///
/// A draw is worth offering when the model can tell what it shows: a picture with words of its own,
/// not a heading. One per guide, so the choice is between clues rather than between a guide's slides.
/// Those PlonkIt has a tip like are left out (<see cref="MainGuideOverlap"/>) — the question asked for
/// what it does not carry.
///
/// Costs no AI request, and never fails an answer: without a draw the question is answered from what
/// its search found, as before.
/// </summary>
public sealed partial class ClueSampler(
    IKnowledgeIndex knowledgeIndex,
    MainGuideOverlap mainGuide,
    IOptions<AiRetrievalConfiguration> retrievalConfiguration,
    ILogger<ClueSampler> logger)
{
    /// <summary>
    /// Chunks drawn for each clue wanted. Most of a draw goes unused: pictures under a bare heading,
    /// a second slide of a guide already drawn, a clue PlonkIt has too.
    /// </summary>
    private const int DrawnPerClue = 8;

    /// <summary>The fewest characters of text a picture needs for the model to say what it shows.</summary>
    private const int MinClueLength = 60;

    /// <summary>The clues drawn, at most <see cref="AiLesserKnownConfiguration.RandomPicks"/>; empty when off or when the draw fails.</summary>
    public Task<IReadOnlyList<KnowledgeHit>> DrawAsync(CancellationToken cancellationToken)
    {
        var settings = retrievalConfiguration.Value.LesserKnown;
        return !settings.Enabled || settings.RandomPicks <= 0
            ? Task.FromResult<IReadOnlyList<KnowledgeHit>>([])
            : DrawAsync(settings.RandomPicks, excludeMainGuideClues: true, cancellationToken);
    }

    /// <summary>
    /// At most <paramref name="count"/> clues drawn, whatever the question settings say: what the clue of
    /// the day asks for. With <paramref name="excludeMainGuideClues"/> off, a clue PlonkIt has a tip like
    /// is kept, and marked.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeHit>> DrawAsync(int count, bool excludeMainGuideClues, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return [];
        }

        try
        {
            var drawn = await knowledgeIndex
                .SampleAsync(
                    new KnowledgeSample(count * DrawnPerClue, KnowledgeChunkKind.Image, MainGuideOverlap.MainGuide),
                    cancellationToken)
                .ConfigureAwait(false) ?? [];

            var clues = drawn
                .Where(IsAClue)
                .DistinctBy(hit => (hit.SourceType, hit.SourceKey))
                .ToList();

            var marked = await mainGuide.MarkAsync(clues, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<KnowledgeHit> picks = [.. marked.Where(hit => !excludeMainGuideClues || !hit.AlsoInMainGuide).Take(count)];

            LogDrawn(logger, picks.Count, drawn.Count, clues.Count, marked.Count(hit => hit.AlsoInMainGuide));
            return picks;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
            return [];
        }
    }

    /// <summary>
    /// The clues drawn, then what the search found, as many as fit the excerpts a question is offered.
    /// The draw leads: for a question that names no subject, what the search found is whatever reads
    /// like the question, and the excerpts numbered first are the ones a model answers from.
    /// </summary>
    public static IReadOnlyList<KnowledgeHit> Lead(
        IReadOnlyList<KnowledgeHit> drawn,
        IReadOnlyList<KnowledgeHit> found,
        int excerptLimit)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(found);

        if (drawn.Count == 0)
        {
            return found;
        }

        var ids = drawn.Select(hit => hit.Id).ToHashSet();
        return
        [
            .. drawn,
            .. found.Where(hit => !ids.Contains(hit.Id)).Take(Math.Max(0, excerptLimit - drawn.Count))
        ];
    }

    /// <summary>Whether a drawn chunk is a clue the model can tell from its text: a picture with words of its own.</summary>
    public static bool IsAClue(KnowledgeHit hit)
    {
        if (hit.Id == Guid.Empty || hit.ImageUrl is null || string.IsNullOrWhiteSpace(hit.SourceKey) || hit.IsUndescribedPicture)
        {
            return false;
        }

        // A long path of headings names a picture well enough to be found by, but tells nothing about it.
        var text = string.Join(' ', hit.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        return text.Length >= MinClueLength
               && !string.Equals(text, hit.SectionPath, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(text.Replace(" — ", " > ", StringComparison.Ordinal), hit.SectionPath, StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(LogLevel.Information,
        "The question names nothing to look for: offering {Picks} picture clue(s) drawn at random ({Drawn} drawn, {Clues} of them a clue from a guide of its own, {OnMainGuide} told by PlonkIt too).")]
    static partial void LogDrawn(ILogger logger, int picks, int drawn, int clues, int onMainGuide);

    [LoggerMessage(LogLevel.Warning, "Drawing picture clues at random failed; the question is answered from its search alone.")]
    static partial void LogFailed(ILogger logger, Exception exception);
}
