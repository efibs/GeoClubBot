namespace UseCases.OutputPorts.Projections;

public sealed record HistoryEntryProjection(
    string UserId,
    int Xp,
    DateTimeOffset Timestamp,
    string? MemberNickname,
    DateTimeOffset? MemberJoinedAt);
