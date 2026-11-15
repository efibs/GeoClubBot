namespace UseCases.UseCases.AI.Progress;

/// <summary>What an answer is busy with, as a member waiting for it is told (#481).</summary>
public enum AiProgressKind
{
    /// <summary>The question's wordings, and the screenshot when there is one, become vectors.</summary>
    Embedding,

    /// <summary>The same, with a screenshot among the inputs.</summary>
    EmbeddingScreenshot,

    /// <summary>The query planner rewrites the question for the search.</summary>
    PlanningSearch,

    /// <summary>The index is searched for the question.</summary>
    SearchingGuides,

    /// <summary>Pl@ntNet names the plant in the screenshot.</summary>
    IdentifyingPlant,

    /// <summary>GBIF is asked where a plant grows; the detail is the name looked up.</summary>
    LookingUpPlant,

    /// <summary>A plant's range map is drawn; the detail is its name.</summary>
    DrawingRangeMap,

    /// <summary>The model is asked to answer.</summary>
    Thinking,

    /// <summary>The first models failed, and others are asked.</summary>
    TryingAnotherModel,

    /// <summary>The model is asked again with what its tools found.</summary>
    ThinkingAboutFindings,

    /// <summary>A later call failed, and the question is asked once more with everything gathered.</summary>
    AnsweringWithWhatItHas,

    /// <summary>The model searches the guides itself; the detail is its query.</summary>
    SearchingGuidesFor,

    /// <summary>The model reads a whole guide section; the detail is its label.</summary>
    ReadingGuide,

    /// <summary>A map of regions is drawn; the detail is the country.</summary>
    DrawingRegionMap,

    /// <summary>The challenge's results are read from GeoGuessr.</summary>
    ReadingChallenge,

    /// <summary>The rounds and guesses are placed in their regions.</summary>
    PlacingRounds,

    /// <summary>GeoNN looks at the rounds' panoramas.</summary>
    ReadingPanoramas,

    /// <summary>The answer, and its pictures, are posted.</summary>
    Posting
}

/// <param name="Detail">What the step is about — a plant, a query, a guide — as the model or the question wrote it; null when nothing.</param>
public sealed record AiProgressStep(AiProgressKind Kind, string? Detail, bool Done);

/// <summary>
/// The steps one answer has taken and is taking. Thread-safe: retrieval, the plant look-aheads and a
/// round's tool calls run at once, and each reports its own step.
/// </summary>
public sealed class AiProgressLog
{
    /// <summary>Steps kept at most; an answer takes a dozen, so this only bounds a runaway.</summary>
    public const int MaxSteps = 64;

    private readonly Lock _gate = new();
    private readonly List<Entry> _steps = [];

    /// <summary>
    /// Raised after every change, outside the lock, on the thread that made it. A handler must be quick
    /// and must not throw: the step that changed is part of the answer.
    /// </summary>
    public event Action? Changed;

    /// <summary>Starts a step, done when the returned handle is disposed.</summary>
    public IDisposable Begin(AiProgressKind kind, string? detail = null)
    {
        var entry = new Entry(this, kind, string.IsNullOrWhiteSpace(detail) ? null : detail.Trim());
        lock (_gate)
        {
            if (_steps.Count >= MaxSteps)
            {
                return NoStep.Instance;
            }

            _steps.Add(entry);
        }

        Changed?.Invoke();
        return entry;
    }

    /// <summary>The steps so far, in the order they began.</summary>
    public IReadOnlyList<AiProgressStep> Snapshot()
    {
        lock (_gate)
        {
            return [.. _steps.Select(step => new AiProgressStep(step.Kind, step.Detail, step.Done))];
        }
    }

    private void Finish(Entry entry)
    {
        lock (_gate)
        {
            if (entry.Done)
            {
                return;
            }

            entry.Done = true;
        }

        Changed?.Invoke();
    }

    private sealed class Entry(AiProgressLog log, AiProgressKind kind, string? detail) : IDisposable
    {
        public AiProgressKind Kind { get; } = kind;

        public string? Detail { get; } = detail;

        /// <summary>Read and written under the log's lock.</summary>
        public bool Done { get; set; }

        public void Dispose() => log.Finish(this);
    }
}

/// <summary>
/// The log of the answer being made on this flow of execution, so a step deep in a singleton (a GBIF
/// lookup, a tool call) can be reported without every signature in between carrying it. Set by
/// whoever shows the progress (<see cref="Track"/>); without one, every step is a no-op — the clue of
/// the day, the retrieval probe and tests report to nobody.
/// </summary>
public static class AiProgress
{
    private static readonly AsyncLocal<AiProgressLog?> CurrentLog = new();

    /// <summary>The log steps go to here, or null.</summary>
    public static AiProgressLog? Current => CurrentLog.Value;

    /// <summary>
    /// Sends the steps of this flow, and of everything it starts, to <paramref name="log"/> until the
    /// handle is disposed. Called from a synchronous method, so the value set holds in the caller.
    /// </summary>
    public static IDisposable Track(AiProgressLog? log)
    {
        var previous = CurrentLog.Value;
        CurrentLog.Value = log;
        return new Restore(previous);
    }

    /// <summary>Starts a step of the current answer, done when the handle is disposed.</summary>
    public static IDisposable Step(AiProgressKind kind, string? detail = null) =>
        CurrentLog.Value?.Begin(kind, detail) ?? NoStep.Instance;

    private sealed class Restore(AiProgressLog? previous) : IDisposable
    {
        public void Dispose() => CurrentLog.Value = previous;
    }
}

internal sealed class NoStep : IDisposable
{
    public static NoStep Instance { get; } = new();

    public void Dispose()
    {
    }
}
