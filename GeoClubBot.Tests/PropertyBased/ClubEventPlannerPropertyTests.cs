using CsCheck;
using FluentAssertions;
using UseCases.UseCases.ClubEvents;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// The planner folds a batch of pushed joins and leaves into where each member ends up. These walk
/// random members through random clubs from a random stored club, emit the events GeoGuessr would —
/// a switch as a leave and a join in either order, since they come from two sockets — interleave the
/// members, and check that applying the plan the way <see cref="ApplyClubEventsHandler"/> does leaves
/// everyone where the walk ended.
/// </summary>
public sealed class ClubEventPlannerPropertyTests
{
    private static readonly Guid?[] Clubs =
    [
        null,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
    ];

    private static readonly ClubEventPlanOptions AllOn = new(true, true, true, true);

    /// <summary>One member: the stored club, the clubs they then go through, and which switches arrive join-first.</summary>
    private sealed record Walk(string UserId, Guid? Stored, IReadOnlyList<Guid?> Steps, IReadOnlyList<bool> JoinFirst)
    {
        public Guid? Final => Steps.Count == 0 ? Stored : Steps[^1];
    }

    private static readonly Gen<Walk[]> GenWalks =
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 3].Array[0, 6], Gen.Bool.Array[6])
            .Array[1, 5]
            .Select(raw => raw.Select((w, i) =>
            {
                var steps = new List<Guid?>();
                var current = Clubs[w.Item1];
                foreach (var index in w.Item2)
                {
                    if (Clubs[index] != current)
                    {
                        current = Clubs[index];
                        steps.Add(current);
                    }
                }

                return new Walk($"user-{i}", Clubs[w.Item1], steps, w.Item3);
            }).ToArray());

    /// <summary>A member's events, in the order the bot receives them.</summary>
    private static List<ClubPushEvent> EventsOf(Walk walk)
    {
        var events = new List<ClubPushEvent>();
        var current = walk.Stored;
        for (var i = 0; i < walk.Steps.Count; i++)
        {
            var next = walk.Steps[i];
            ClubPushEvent? leave = current is { } from ? new ClubMemberLeftPush(from, default, walk.UserId) : null;
            ClubPushEvent? join = next is { } to ? new ClubMemberJoinedPush(to, default, walk.UserId, "nick", 0, DateTimeOffset.UnixEpoch) : null;

            if (walk.JoinFirst[i])
            {
                AddIfAny(events, join);
                AddIfAny(events, leave);
            }
            else
            {
                AddIfAny(events, leave);
                AddIfAny(events, join);
            }

            current = next;
        }

        return events;
    }

    private static void AddIfAny(List<ClubPushEvent> events, ClubPushEvent? pushEvent)
    {
        if (pushEvent is not null)
        {
            events.Add(pushEvent);
        }
    }

    /// <summary>Merges the members' events in a random order that keeps each member's own order, numbering arrival.</summary>
    private static List<ClubPushEvent> Interleave(IEnumerable<List<ClubPushEvent>> perUser, int seed)
    {
        var random = new Random(seed);
        var queues = perUser.Select(e => new Queue<ClubPushEvent>(e)).Where(q => q.Count > 0).ToList();
        var merged = new List<ClubPushEvent>();
        while (queues.Count > 0)
        {
            var queue = queues[random.Next(queues.Count)];
            merged.Add(queue.Dequeue() with { Order = new PushOrder(null, merged.Count + 1) });
            if (queue.Count == 0)
            {
                queues.Remove(queue);
            }
        }

        return merged;
    }

    /// <summary>What <see cref="ApplyClubEventsHandler"/> stores: a join as is, a leave only from a club it names.</summary>
    private static Dictionary<string, Guid?> Apply(ClubEventPlan plan, IEnumerable<Walk> walks)
    {
        var stored = walks.ToDictionary(w => w.UserId, w => w.Stored);
        foreach (var change in plan.MemberChanges)
        {
            switch (change)
            {
                case MemberJoined joined:
                    stored[joined.UserId] = joined.ClubId;
                    break;
                case MemberLeft left when stored[left.UserId] is { } club && left.FromClubs.Contains(club):
                    stored[left.UserId] = null;
                    break;
            }
        }

        return stored;
    }

    [Fact]
    public void Applying_the_plan_leaves_every_member_where_their_walk_ended() =>
        Gen.Select(GenWalks, Gen.Int).Sample((walks, seed) =>
        {
            var plan = ClubEventPlanner.Plan(Interleave(walks.Select(EventsOf), seed), AllOn);

            Apply(plan, walks).Should().Equal(walks.ToDictionary(w => w.UserId, w => w.Final));
        });

    [Fact]
    public void Every_member_gets_at_most_one_change() =>
        Gen.Select(GenWalks, Gen.Int).Sample((walks, seed) =>
        {
            var plan = ClubEventPlanner.Plan(Interleave(walks.Select(EventsOf), seed), AllOn);

            plan.MemberChanges.Select(c => c.UserId).Should().OnlyHaveUniqueItems();
        });

    [Fact]
    public void The_plan_does_not_depend_on_how_members_interleave() =>
        Gen.Select(GenWalks, Gen.Int, Gen.Int).Sample((walks, seed1, seed2) =>
        {
            var first = ClubEventPlanner.Plan(Interleave(walks.Select(EventsOf), seed1), AllOn);
            var second = ClubEventPlanner.Plan(Interleave(walks.Select(EventsOf), seed2), AllOn);

            second.MemberChanges.Should().BeEquivalentTo(first.MemberChanges, o => o.WithStrictOrdering());
        });

    [Fact]
    public void GeoGuessrs_timestamps_put_any_arrival_order_right() =>
        Gen.Select(GenWalks, Gen.Int).Sample((walks, seed) =>
        {
            // Stamped in the true order (switches leave-first), then delivered in any order at all.
            var stamped = walks
                .SelectMany(w => EventsOf(w with { JoinFirst = new bool[w.JoinFirst.Count] }))
                .Select((e, i) => e with { Order = new PushOrder(DateTimeOffset.UnixEpoch.AddSeconds(i), 0) })
                .ToList();
            var random = new Random(seed);
            var arrived = stamped
                .OrderBy(_ => random.Next())
                .Select((e, i) => e with { Order = e.Order with { Sequence = i + 1 } })
                .ToList();

            var plan = ClubEventPlanner.Plan(arrived, AllOn);

            Apply(plan, walks).Should().Equal(walks.ToDictionary(w => w.UserId, w => w.Final));
        });

    [Fact]
    public void A_roster_resync_carries_no_member_changes() =>
        Gen.Select(GenWalks, Gen.Int, Gen.Int[1, 3]).Sample((walks, seed, club) =>
        {
            var events = Interleave(walks.Select(EventsOf), seed);
            events.Add(new ClubRosterChangedPush(Clubs[club]!.Value, new PushOrder(null, events.Count + 1)));

            var plan = ClubEventPlanner.Plan(events, AllOn);

            plan.ResyncRosters.Should().BeTrue();
            plan.MemberChanges.Should().BeEmpty();
        });
}
