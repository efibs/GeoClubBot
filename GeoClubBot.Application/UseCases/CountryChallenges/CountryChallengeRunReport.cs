namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// What a run did, phase by phase. A phase that failed outright is <c>null</c>; its error was logged,
/// and the phases after it ran regardless.
/// </summary>
public sealed record CountryChallengeRunReport(
    DateOnly Date,
    IReadOnlyList<string> Warnings,
    CountryChallengeEvaluationOutcome? Evaluation,
    CountryChallengeLeaderboardOutcome? Leaderboard,
    CountryChallengeAnnouncementOutcome? Announcement);

/// <summary>Challenges whose results were evaluated, and those that will be retried at the next run.</summary>
public sealed record CountryChallengeEvaluationOutcome(IReadOnlyList<string> Evaluated, IReadOnlyList<string> Failed)
{
    public static readonly CountryChallengeEvaluationOutcome Nothing = new([], []);
}

public enum CountryChallengeLeaderboardStatus
{
    NotDue,
    AlreadyPosted,
    Empty,
    Posted,
    Failed
}

public sealed record CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus Status);

/// <summary>
/// The day's challenges that were announced, those already announced by an earlier run that day, and
/// those that could not be created or announced — a later run the same day retries these.
/// </summary>
public sealed record CountryChallengeAnnouncementOutcome(
    IReadOnlyList<string> Announced,
    IReadOnlyList<string> AlreadyPosted,
    IReadOnlyList<string> Failed)
{
    public static readonly CountryChallengeAnnouncementOutcome Nothing = new([], [], []);
}
