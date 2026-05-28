namespace UseCases.OutputPorts.Projections;

public sealed record LatestHistoryEntryProjection(string UserId, int Xp, DateTimeOffset Timestamp);
