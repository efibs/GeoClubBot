namespace UseCases.OutputPorts.Projections;

public sealed record ExcuseProjection(string UserId, DateTimeOffset From, DateTimeOffset To);
