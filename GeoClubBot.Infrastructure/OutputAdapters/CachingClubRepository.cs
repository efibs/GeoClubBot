using Entities;
using Microsoft.Extensions.Caching.Memory;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class CachingClubRepository(EfClubRepository inner, IMemoryCache cache) : IClubRepository
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public Club CreateClub(Club club)
    {
        var created = inner.CreateClub(club);
        InvalidateCacheFor(club);
        return created;
    }

    public async Task<Club> CreateOrUpdateClubAsync(Club club)
    {
        var result = await inner.CreateOrUpdateClubAsync(club).ConfigureAwait(false);
        InvalidateCacheFor(club);
        return result;
    }

    public async Task<Club?> ReadClubByIdAsync(Guid clubId)
    {
        var key = ByIdKey(clubId);
        if (cache.TryGetValue<Club?>(key, out var cached))
        {
            return cached;
        }

        var club = await inner.ReadClubByIdAsync(clubId).ConfigureAwait(false);
        cache.Set(key, club, CacheTtl);
        return club;
    }

    public Task<Club?> ReadForUpdateByIdAsync(Guid clubId)
    {
        // Caller intends to mutate; bypass the cache and return a tracked entity.
        // The mutation is invalidated implicitly on the next CreateOrUpdateClubAsync,
        // but since the level mutation goes through SaveChanges, we also invalidate here
        // so stale reads don't outlive the change.
        cache.Remove(ByIdKey(clubId));
        return inner.ReadForUpdateByIdAsync(clubId);
    }

    public async Task<Club?> ReadClubByNameAsync(string clubName)
    {
        var key = ByNameKey(clubName);
        if (cache.TryGetValue<Club?>(key, out var cached))
        {
            return cached;
        }

        var club = await inner.ReadClubByNameAsync(clubName).ConfigureAwait(false);
        cache.Set(key, club, CacheTtl);
        return club;
    }

    private void InvalidateCacheFor(Club club)
    {
        cache.Remove(ByIdKey(club.ClubId));
        cache.Remove(ByNameKey(club.Name));
    }

    private static string ByIdKey(Guid clubId) => $"clubs:{clubId}";
    private static string ByNameKey(string clubName) => $"clubs:by-name:{clubName}";
}
