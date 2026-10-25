namespace Entities;

/// <summary>A club member's mission completion times, for the weekly report.</summary>
public sealed record ClubMemberMissionCompletion(string Nickname, string UserId, MissionCompletionTimes Times);
