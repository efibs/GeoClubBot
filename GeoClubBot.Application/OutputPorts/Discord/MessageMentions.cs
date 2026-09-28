namespace UseCases.OutputPorts.Discord;

/// <summary>
/// Who a message is allowed to ping. Anything not listed here renders as a mention but notifies no one,
/// which is what keeps text that came from outside — a GeoGuessr nickname, say — from pinging anybody.
/// </summary>
public sealed record MessageMentions(
    IReadOnlyCollection<ulong> RoleIds,
    IReadOnlyCollection<ulong> UserIds,
    bool Everyone)
{
    public static readonly MessageMentions None = new([], [], false);

    public bool IsEmpty => RoleIds.Count == 0 && UserIds.Count == 0 && !Everyone;
}

/// <summary>How long a thread stays open without activity — the durations Discord supports.</summary>
public enum ThreadAutoArchive
{
    OneHour,
    OneDay,
    ThreeDays,
    OneWeek
}
