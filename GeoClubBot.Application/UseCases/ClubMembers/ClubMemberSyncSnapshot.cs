namespace UseCases.UseCases.ClubMembers;

/// <summary>
/// Flat transport object for a club-member's intended state (typically derived from the
/// GeoGuessr API or computed from a sync diff). The handler is responsible for upserting
/// the user, then the member, with these values.
/// </summary>
public sealed record ClubMemberSyncSnapshot(
    string UserId,
    string Nickname,
    Guid? TargetClubId,
    int Xp,
    DateTimeOffset JoinedAt);
