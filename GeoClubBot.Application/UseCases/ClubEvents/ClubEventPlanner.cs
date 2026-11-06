using Configuration;

namespace UseCases.UseCases.ClubEvents;

/// <summary>Which pushed events are acted on (see <see cref="GeoGuessrSocketConfiguration"/>).</summary>
public sealed record ClubEventPlanOptions(bool UsePayloads, bool MemberEvents, bool MissionBoardEvents, bool ClubUpdatedEvents)
{
    public static ClubEventPlanOptions From(GeoGuessrSocketConfiguration config) =>
        new(config.UsePayloads, config.MemberEvents, config.MissionBoardEvents, config.ClubUpdatedEvents);
}

/// <summary>Where a member ends up after a batch's events.</summary>
public abstract record MemberChange(string UserId);

/// <summary>The member is in <see cref="ClubId"/>, with the details the join carried.</summary>
public sealed record MemberJoined(string UserId, Guid ClubId, string Nickname, int Xp, DateTimeOffset JoinedAt)
    : MemberChange(UserId);

/// <summary>
/// The member is in none of the clubs: they left every one of <see cref="FromClubs"/>. Applied only if
/// the stored membership is one of them; otherwise the leave is older than what is stored.
/// </summary>
public sealed record MemberLeft(string UserId, IReadOnlySet<Guid> FromClubs) : MemberChange(UserId);

/// <summary>What a batch of pushed events asks for. Applied by <see cref="ApplyClubEventsCommand"/>.</summary>
/// <param name="ResyncRosters">Read every club's roster again; <see cref="MemberChanges"/> is then empty.</param>
/// <param name="BoardsToRecord">Clubs whose current mission board is read again and its claims recorded.</param>
/// <param name="LevelChecks">Clubs whose level is read again.</param>
/// <param name="LevelUpdates">Pushed levels, for clubs not in <see cref="LevelChecks"/>.</param>
/// <param name="Renames">Pushed names; empty when <see cref="ResyncRosters"/>, which renames every club.</param>
public sealed record ClubEventPlan(
    bool ResyncRosters,
    IReadOnlyList<MemberChange> MemberChanges,
    IReadOnlySet<Guid> BoardsToRecord,
    IReadOnlySet<Guid> LevelChecks,
    IReadOnlyDictionary<Guid, int> LevelUpdates,
    IReadOnlyDictionary<Guid, string> Renames)
{
    public bool IsEmpty =>
        !ResyncRosters && MemberChanges.Count == 0 && BoardsToRecord.Count == 0
        && LevelChecks.Count == 0 && LevelUpdates.Count == 0 && Renames.Count == 0;
}

/// <summary>
/// Turns a batch of <see cref="ClubPushEvent"/>s into one <see cref="ClubEventPlan"/>. Pure, so the
/// rules below are tested on their own.
/// </summary>
/// <remarks>
/// Each user's joins and leaves are folded into where they end up. A join puts them in that club. A
/// leave takes them out — unless they are, by then, in a different club: GeoGuessr reports a switch as
/// a leave on one club's socket and a join on the other's, and the two can arrive in either order.
/// Every club a user left is remembered, so a user who switched and then left the new club too is
/// still taken out of the club the database has them in.
/// <para>
/// Anything that needs a re-read of a roster (a refresh, a member event without usable data, or
/// payloads switched off) makes the batch a full roster sync, which then supersedes the batch's
/// member changes: the sync reads the current state, and only a sync of every club recognises a
/// switch.
/// </para>
/// </remarks>
public static class ClubEventPlanner
{
    public static ClubEventPlan Plan(IReadOnlyList<ClubPushEvent> events, ClubEventPlanOptions options)
    {
        var resyncRosters = false;
        var boardsToRecord = new HashSet<Guid>();
        var levelChecks = new HashSet<Guid>();
        var levelUpdates = new Dictionary<Guid, (int Level, PushOrder Order)>();
        var renames = new Dictionary<Guid, (string Name, PushOrder Order)>();
        var memberEvents = new List<ClubPushEvent>();

        foreach (var pushEvent in events)
        {
            switch (pushEvent)
            {
                case ClubRefreshRequested refresh:
                    resyncRosters = true;
                    boardsToRecord.Add(refresh.ClubId);
                    levelChecks.Add(refresh.ClubId);
                    break;

                case ClubRosterChangedPush when options.MemberEvents:
                    resyncRosters = true;
                    break;

                case ClubMemberJoinedPush or ClubMemberLeftPush when options.MemberEvents:
                    if (options.UsePayloads)
                    {
                        memberEvents.Add(pushEvent);
                    }
                    else
                    {
                        resyncRosters = true;
                    }

                    break;

                case ClubUpdatedPush updated when options.ClubUpdatedEvents:
                    if (updated.Level is { } level)
                    {
                        KeepLatest(levelUpdates, updated.ClubId, level, updated.Order);
                    }

                    if (!string.IsNullOrWhiteSpace(updated.Name))
                    {
                        KeepLatest(renames, updated.ClubId, updated.Name, updated.Order);
                    }

                    break;

                case ClubMissionBoardUpdatedPush board when options.MissionBoardEvents:
                    boardsToRecord.Add(board.ClubId);
                    break;
            }
        }

        IReadOnlyList<MemberChange> memberChanges = resyncRosters ? [] : FoldMembers(memberEvents);

        return new ClubEventPlan(
            resyncRosters,
            memberChanges,
            boardsToRecord,
            levelChecks,
            // A club whose level is read again needs no pushed level: the read is at least as new.
            levelUpdates
                .Where(kv => !levelChecks.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Level),
            resyncRosters ? new Dictionary<Guid, string>() : renames.ToDictionary(kv => kv.Key, kv => kv.Value.Name));
    }

    private static List<MemberChange> FoldMembers(List<ClubPushEvent> memberEvents)
    {
        var changes = new List<MemberChange>();

        foreach (var userEvents in memberEvents.GroupBy(UserIdOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            MemberJoined? inClub = null;
            var leftSomewhere = false;
            var leftClubs = new HashSet<Guid>();

            foreach (var pushEvent in InOrder(userEvents.ToList()))
            {
                switch (pushEvent)
                {
                    case ClubMemberJoinedPush joined:
                        inClub = new MemberJoined(joined.UserId, joined.ClubId, joined.Nickname, joined.Xp, joined.JoinedAt);
                        break;

                    case ClubMemberLeftPush left:
                        leftClubs.Add(left.ClubId);
                        // A leave of another club than the one they are in by now is the older half
                        // of a switch.
                        if (inClub is null || inClub.ClubId == left.ClubId)
                        {
                            inClub = null;
                            leftSomewhere = true;
                        }

                        break;
                }
            }

            if (inClub is not null)
            {
                changes.Add(inClub);
            }
            else if (leftSomewhere)
            {
                changes.Add(new MemberLeft(userEvents.Key, leftClubs));
            }
        }

        return changes;
    }

    /// <summary>
    /// GeoGuessr's timestamps when every event has one (two sockets don't deliver in a shared order),
    /// otherwise the order of arrival.
    /// </summary>
    private static IEnumerable<ClubPushEvent> InOrder(List<ClubPushEvent> userEvents) =>
        userEvents.All(e => e.Order.ServerTime is not null)
            ? userEvents.OrderBy(e => e.Order.ServerTime).ThenBy(e => e.Order.Sequence)
            : userEvents.OrderBy(e => e.Order.Sequence);

    private static string UserIdOf(ClubPushEvent pushEvent) => pushEvent switch
    {
        ClubMemberJoinedPush joined => joined.UserId,
        ClubMemberLeftPush left => left.UserId,
        _ => throw new ArgumentOutOfRangeException(nameof(pushEvent), pushEvent, "Not a member event."),
    };

    private static void KeepLatest<T>(Dictionary<Guid, (T Value, PushOrder Order)> latest, Guid clubId, T value, PushOrder order)
    {
        if (!latest.TryGetValue(clubId, out var current) || IsLater(order, current.Order))
        {
            latest[clubId] = (value, order);
        }
    }

    private static bool IsLater(PushOrder candidate, PushOrder current) =>
        candidate.ServerTime is { } candidateTime && current.ServerTime is { } currentTime && candidateTime != currentTime
            ? candidateTime > currentTime
            : candidate.Sequence > current.Sequence;
}
