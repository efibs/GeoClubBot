namespace Entities;

/// <summary>
/// One country challenge the bot created and announced. It is the history the pool rotation reads,
/// the key that stops a challenge from being posted twice on the same day, and the pending work the
/// evaluation picks up once its results are due. The country, map and settings are recorded as played,
/// so the results still describe the challenge correctly after the configuration has changed.
/// </summary>
public class CountryChallengePost : BaseEntity
{
    public int Id { get; private set; }

    /// <summary>Name of the configured challenge. Together with <see cref="Date"/> it is unique.</summary>
    public string ChallengeName { get; private set; } = string.Empty;

    /// <summary>The day the challenge was posted for, in the configured time zone.</summary>
    public DateOnly Date { get; private set; }

    public string Country { get; private set; } = string.Empty;

    public string? CountryCode { get; private set; }

    public string MapId { get; private set; } = string.Empty;

    public string? MapName { get; private set; }

    public int TimeLimit { get; private set; }

    public bool ForbidMoving { get; private set; }

    public bool ForbidRotating { get; private set; }

    public bool ForbidZooming { get; private set; }

    /// <summary>The GeoGuessr challenge token.</summary>
    public string ChallengeId { get; private set; } = string.Empty;

    public ulong ChannelId { get; private set; }

    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>The first day the results may be evaluated. <c>null</c> when no results were wanted.</summary>
    public DateOnly? ResultsDueOn { get; private set; }

    /// <summary>When the results were evaluated. <c>null</c> while they are still pending.</summary>
    public DateTimeOffset? EvaluatedAt { get; private set; }

    public static CountryChallengePost Create(
        string challengeName,
        DateOnly date,
        string country,
        string? countryCode,
        string mapId,
        string? mapName,
        int timeLimit,
        bool forbidMoving,
        bool forbidRotating,
        bool forbidZooming,
        string challengeId,
        ulong channelId,
        DateTimeOffset postedAt,
        DateOnly? resultsDueOn)
    {
        return new CountryChallengePost
        {
            ChallengeName = challengeName,
            Date = date,
            Country = country,
            CountryCode = countryCode,
            MapId = mapId,
            MapName = mapName,
            TimeLimit = timeLimit,
            ForbidMoving = forbidMoving,
            ForbidRotating = forbidRotating,
            ForbidZooming = forbidZooming,
            ChallengeId = challengeId,
            ChannelId = channelId,
            PostedAt = postedAt,
            ResultsDueOn = resultsDueOn
        };
    }

    public void MarkEvaluated(DateTimeOffset evaluatedAt)
    {
        EvaluatedAt = evaluatedAt;
    }

    private CountryChallengePost()
    {
    }
}
