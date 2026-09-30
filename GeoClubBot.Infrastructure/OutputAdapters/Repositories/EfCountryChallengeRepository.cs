using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfCountryChallengeRepository(GeoClubBotDbContext dbContext) : ICountryChallengeRepository
{
    public async Task<List<CountryChallengePost>> ReadPostsOnAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await dbContext.CountryChallengePosts
            .AsNoTracking()
            .Where(p => p.Date == date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<string>> ReadCountryHistoryAsync(string challengeName, CancellationToken cancellationToken = default)
    {
        return await dbContext.CountryChallengePosts
            .AsNoTracking()
            .Where(p => p.ChallengeName == challengeName)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Id)
            .Select(p => p.Country)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void AddPosts(IEnumerable<CountryChallengePost> posts)
    {
        dbContext.CountryChallengePosts.AddRange(posts);
    }

    public void RemovePosts(IEnumerable<CountryChallengePost> posts)
    {
        dbContext.CountryChallengePosts.RemoveRange(posts);
    }

    public async Task<List<CountryChallengePost>> ReadPostsDueForEvaluationAsync(
        DateOnly today,
        DateOnly notBefore,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.CountryChallengePosts
            .Where(p => p.EvaluatedAt == null
                        && p.ResultsDueOn != null
                        && p.ResultsDueOn <= today
                        && p.ResultsDueOn >= notBefore)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.ChallengeName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<CountryChallengePost>> ReadPendingPostsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.CountryChallengePosts
            .Where(p => p.EvaluatedAt == null && p.ResultsDueOn != null)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.ChallengeName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void AddAwards(IEnumerable<CountryChallengePointAward> awards)
    {
        dbContext.CountryChallengePointAwards.AddRange(awards);
    }

    public async Task<List<CountryChallengePointAward>> ReadAwardsAsync(string season, CancellationToken cancellationToken = default)
    {
        return await dbContext.CountryChallengePointAwards
            .AsNoTracking()
            .Where(a => a.Season == season)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReplaceImportedAwardsAsync(
        string season,
        IEnumerable<CountryChallengePointAward> awards,
        CancellationToken cancellationToken = default)
    {
        var previous = await dbContext.CountryChallengePointAwards
            .Where(a => a.Season == season && a.Source == CountryChallengePointSource.Import)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.CountryChallengePointAwards.RemoveRange(previous);
        dbContext.CountryChallengePointAwards.AddRange(awards);
    }

    public async Task<bool> IsLeaderboardPostedOnAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await dbContext.CountryChallengeLeaderboardPosts
            .AsNoTracking()
            .AnyAsync(p => p.Date == date, cancellationToken)
            .ConfigureAwait(false);
    }

    public void AddLeaderboardPost(CountryChallengeLeaderboardPost post)
    {
        dbContext.CountryChallengeLeaderboardPosts.Add(post);
    }

    public void RemoveLeaderboardPost(CountryChallengeLeaderboardPost post)
    {
        dbContext.CountryChallengeLeaderboardPosts.Remove(post);
    }
}
