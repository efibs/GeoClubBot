using Entities;

namespace Infrastructure.OutputAdapters.Repositories;

internal static class StrikeQueryableExtensions
{
    public static IQueryable<ClubMemberStrike> WhereActive(this IQueryable<ClubMemberStrike> source)
    {
        return source.Where(s => s.Revoked == false);
    }
}
