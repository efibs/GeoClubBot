using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.CountryChallenges.Configuration;

/// <summary>
/// The country challenge file with every inherited value filled in and every rule checked. Nothing in
/// here is optional any more, so the use cases never have to know which level a value came from.
/// </summary>
public sealed record CountryChallengePlan(
    AnnouncementPlan Announcement,
    ResultsPlan Results,
    LeaderboardPlan Leaderboard,
    IReadOnlyList<ChallengePlan> Challenges,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Disabled challenges that resolved cleanly. They are never posted, but a challenge that was posted
    /// before it was disabled still has its results evaluated with its own settings.
    /// </summary>
    public IReadOnlyList<ChallengePlan> DisabledChallenges { get; init; } = [];

    /// <summary>The enabled challenges scheduled for <paramref name="date"/>, in file order.</summary>
    public IReadOnlyList<ChallengePlan> ChallengesOn(DateOnly date) =>
        Challenges.Where(c => c.IsScheduledOn(date)).ToList();

    /// <summary>
    /// The results settings of a challenge by name. A challenge that has since been removed from the
    /// file falls back to the top-level results settings.
    /// </summary>
    public ChallengeResultsPlan ResultsFor(string challengeName) =>
        Challenges.Concat(DisabledChallenges)
            .FirstOrDefault(c => string.Equals(c.Name, challengeName, StringComparison.OrdinalIgnoreCase))
            ?.Results
        ?? Results.Defaults;
}

public sealed record GameSettings(int TimeLimit, bool ForbidMoving, bool ForbidRotating, bool ForbidZooming);

public sealed record AnnouncementPlan(string Message, ThreadPlan Thread);

public sealed record ThreadPlan(bool Enabled, string Name, ThreadAutoArchive AutoArchive);

/// <summary>The results settings of a whole run, plus the defaults a removed challenge falls back to.</summary>
public sealed record ResultsPlan(string Message, IReadOnlyList<ulong> MentionRoleIds, ChallengeResultsPlan Defaults);

public sealed record ChallengeResultsPlan(
    bool Enabled,
    int AfterDays,
    bool Post,
    ulong? ChannelId,
    int Top,
    string Entry,
    IReadOnlyList<int> Points,
    IReadOnlyList<ulong> RoleIds)
{
    /// <summary>How many highscores to read: enough to list, to award points to, and to hand roles to.</summary>
    public int HighscoreLimit => Math.Max(Top, Math.Max(Points.Count, RoleIds.Count));
}

public sealed record LeaderboardPlan(
    bool Enabled,
    string Season,
    IReadOnlySet<DayOfWeek> Days,
    IReadOnlySet<DateOnly> Dates,
    ulong ChannelId,
    int Top,
    string Message,
    IReadOnlyList<ulong> RoleIds,
    IReadOnlyList<ulong> MentionRoleIds)
{
    public bool IsDueOn(DateOnly date) => Enabled && (Days.Contains(date.DayOfWeek) || Dates.Contains(date));
}

/// <param name="Picks">How many different countries are played each day the challenge runs.</param>
public sealed record ChallengePlan(
    string Name,
    IReadOnlySet<DayOfWeek> Days,
    IReadOnlySet<DateOnly> Dates,
    IReadOnlyList<CountryPlan> Countries,
    int Picks,
    ulong ChannelId,
    IReadOnlyList<ulong> MentionRoleIds,
    string Entry,
    ChallengeResultsPlan Results)
{
    public bool IsScheduledOn(DateOnly date) => Days.Contains(date.DayOfWeek) || Dates.Contains(date);
}

public sealed record CountryPlan(string Name, string? Code, string MapId, string? MapName, GameSettings Settings);
