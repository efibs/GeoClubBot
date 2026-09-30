using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.CountryChallenges.Configuration;

// The country challenge file exactly as written. Every value that can be inherited is nullable: null
// means "not set at this level", and CountryChallengePlanResolver fills it in from the level above —
// built-in default, then the top of the file, then the challenge, then a single country of a pool.

public sealed record CountryChallengesFile
{
    /// <summary>Default channel for announcements, results and the leaderboard.</summary>
    public ulong? ChannelId { get; init; }

    public GameSettingsSection? Settings { get; init; }

    public AnnouncementSection? Announcement { get; init; }

    public ResultsSection? Results { get; init; }

    public LeaderboardSection? Leaderboard { get; init; }

    public List<ChallengeSection>? Challenges { get; init; }
}

/// <summary>The GeoGuessr settings a challenge is created with.</summary>
public sealed record GameSettingsSection
{
    /// <summary>Seconds per round; 0 means no limit.</summary>
    public int? TimeLimit { get; init; }

    public bool? ForbidMoving { get; init; }

    public bool? ForbidRotating { get; init; }

    public bool? ForbidZooming { get; init; }
}

public sealed record AnnouncementSection
{
    /// <summary>The whole day's message, wrapping the <c>{{challenges}}</c> entries.</summary>
    public string? Message { get; init; }

    /// <summary>One challenge's part of the day's message.</summary>
    public string? Entry { get; init; }

    public List<ulong>? MentionRoleIds { get; init; }

    public ThreadSection? Thread { get; init; }
}

public sealed record ThreadSection
{
    public bool? Enabled { get; init; }

    public string? Name { get; init; }

    public ThreadAutoArchive? AutoArchive { get; init; }
}

/// <summary>Results settings at the top of the file, including the parts that belong to a whole run.</summary>
public sealed record ResultsSection
{
    public bool? Enabled { get; init; }

    public int? AfterDays { get; init; }

    public bool? Post { get; init; }

    public ulong? ChannelId { get; init; }

    public int? Top { get; init; }

    /// <summary>The whole results message, wrapping the <c>{{results}}</c> entries.</summary>
    public string? Message { get; init; }

    public string? Entry { get; init; }

    public List<int>? Points { get; init; }

    public List<ulong>? RoleIds { get; init; }

    public List<ulong>? MentionRoleIds { get; init; }
}

/// <summary>The results settings one challenge may override.</summary>
public sealed record ChallengeResultsSection
{
    public bool? Enabled { get; init; }

    public int? AfterDays { get; init; }

    public bool? Post { get; init; }

    public ulong? ChannelId { get; init; }

    public int? Top { get; init; }

    public string? Entry { get; init; }

    public List<int>? Points { get; init; }

    public List<ulong>? RoleIds { get; init; }
}

public sealed record LeaderboardSection
{
    public bool? Enabled { get; init; }

    public string? Season { get; init; }

    public List<DayOfWeek>? Days { get; init; }

    public List<DateOnly>? Dates { get; init; }

    public ulong? ChannelId { get; init; }

    public int? Top { get; init; }

    public string? Message { get; init; }

    public List<ulong>? RoleIds { get; init; }

    public List<ulong>? MentionRoleIds { get; init; }
}

public sealed record ChallengeSection
{
    public string? Name { get; init; }

    public bool? Enabled { get; init; }

    public List<DayOfWeek>? Days { get; init; }

    public List<DateOnly>? Dates { get; init; }

    /// <summary>The one country this challenge is always played in. Exclusive with <see cref="Pool"/>.</summary>
    public CountrySection? Country { get; init; }

    /// <summary>The countries this challenge rotates through. Exclusive with <see cref="Country"/>.</summary>
    public List<CountrySection>? Pool { get; init; }

    /// <summary>How many different countries of the pool are played each day it runs. Defaults to 1.</summary>
    public int? Picks { get; init; }

    public ulong? ChannelId { get; init; }

    public List<ulong>? MentionRoleIds { get; init; }

    public string? Entry { get; init; }

    public GameSettingsSection? Settings { get; init; }

    public ChallengeResultsSection? Results { get; init; }
}

public sealed record CountrySection
{
    public string? Name { get; init; }

    /// <summary>ISO 3166-1 alpha-2 code, used for the flag.</summary>
    public string? Code { get; init; }

    public string? MapId { get; init; }

    public string? MapName { get; init; }

    public GameSettingsSection? Settings { get; init; }
}
