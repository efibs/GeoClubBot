namespace UseCases.OutputPorts.GeoGuessr;

public class ReadClubActivitiesItemDto
{
    public required string UserId { get; set; }

    /// <summary>
    /// GeoGuessr's activity type discriminator. Null when the entry predates the field or comes
    /// from a stand-in that does not set it — see <c>ClubActivityKindClassifier</c>, which falls
    /// back to the XP amount in that case.
    /// </summary>
    public int? Type { get; set; }

    public required int XpReward { get; set; }

    public required DateTimeOffset RecordedAt { get; set; }

    /// <summary>Set on the entry that pushed the member over a club level boundary; null otherwise.</summary>
    public int? NewLevel { get; set; }

    /// <summary>Set on club-challenge entries (type 3); null on XP awards.</summary>
    public string? ChallengeToken { get; set; }
}
