using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.RandomDraws;

/// <summary>A server member as the people draws see them.</summary>
/// <param name="IsDeafened">Deafened by themselves or the server: in a voice channel, but not listening.</param>
public sealed record DrawPerson(ulong Id, bool IsBot, bool IsDeafened, IReadOnlySet<ulong> RoleIds);

/// <summary>How several roles of <c>/random role</c> are matched.</summary>
public enum RoleMatch
{
    /// <summary>At least one of the roles.</summary>
    [ChoiceDisplay("Any of them")]
    Any,

    /// <summary>Every one of the roles.</summary>
    [ChoiceDisplay("All of them")]
    All
}

/// <summary>Who a people draw may draw. Pure.</summary>
public static class RandomPeople
{
    /// <summary>The ids of the people who may be drawn: no bots, no one deafened, nobody excluded; by id.</summary>
    public static List<ulong> Eligible(IEnumerable<DrawPerson> people, IReadOnlySet<ulong> excluded) =>
        people
            .Where(person => !person.IsBot && !person.IsDeafened && !excluded.Contains(person.Id))
            .Select(person => person.Id)
            .Distinct()
            .Order()
            .ToList();

    /// <summary>The people holding one (<see cref="RoleMatch.Any"/>) or each (<see cref="RoleMatch.All"/>) of the roles.</summary>
    public static IEnumerable<DrawPerson> WithRoles(IEnumerable<DrawPerson> people, IReadOnlyCollection<ulong> roleIds, RoleMatch match) =>
        people.Where(person => match == RoleMatch.All
            ? roleIds.All(person.RoleIds.Contains)
            : roleIds.Any(person.RoleIds.Contains));

    /// <summary>The excluded ids: the caller when <paramref name="excludeMe"/>, and the one user named.</summary>
    public static HashSet<ulong> Excluded(ulong callerId, bool excludeMe, ulong? excludedUserId)
    {
        var excluded = new HashSet<ulong>();
        if (excludeMe)
        {
            excluded.Add(callerId);
        }

        if (excludedUserId is { } id)
        {
            excluded.Add(id);
        }

        return excluded;
    }
}
