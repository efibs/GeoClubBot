namespace UseCases.UseCases.AI.Retrieval;

/// <summary>
/// Remembers that the query planner recently ran out of time, so the questions after it are not made
/// to wait for it too.
///
/// The planner asks whichever model heads the answer chain, and how fast that is decides whether
/// planning pays. Replayed against the 2026-10-23 feedback export, <c>nemotron-3-ultra</c> planned in
/// 4.4 s (median) and 48 of 49 questions within the 20 s the bot waits; <c>dots-3-note-preview</c>, the
/// head of the chain that day, took 34 s and only 11 of 49 within it. Behind a slow model every
/// question would wait the full timeout and pay a request for nothing. After a timeout the planner
/// therefore sits out a while (<c>AI:Retrieval:QueryPlanner:PauseAfterTimeoutMinutes</c>) and then
/// tries again, by which time the chain may have changed.
///
/// A singleton: the handler that consults it lives for one question.
/// </summary>
public sealed class QueryPlannerPause(TimeProvider timeProvider)
{
    /// <summary>When planning resumes, as UTC ticks; zero when it is not paused.</summary>
    private long _resumesAtTicks;

    /// <summary>When planning resumes, or null when it is not paused.</summary>
    public DateTimeOffset? ResumesAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _resumesAtTicks);
            return ticks > timeProvider.GetUtcNow().UtcTicks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }

    public bool IsPaused => ResumesAt is not null;

    /// <summary>Skips the planner for <paramref name="duration"/> from now. Zero or less pauses nothing.</summary>
    public void PauseFor(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
        {
            Interlocked.Exchange(ref _resumesAtTicks, (timeProvider.GetUtcNow() + duration).UtcTicks);
        }
    }
}
