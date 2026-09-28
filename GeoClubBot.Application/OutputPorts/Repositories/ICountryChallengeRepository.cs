using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface ICountryChallengeRepository
{
    /// <summary>The challenges already posted for <paramref name="date"/>, one per country played.</summary>
    Task<List<CountryChallengePost>> ReadPostsOnAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The countries a challenge was played with, oldest first — the input of the pool rotation.</summary>
    Task<List<string>> ReadCountryHistoryAsync(string challengeName, CancellationToken cancellationToken = default);

    void AddPosts(IEnumerable<CountryChallengePost> posts);

    void RemovePosts(IEnumerable<CountryChallengePost> posts);

    /// <summary>
    /// Tracked posts whose results are due on or before <paramref name="today"/>, have not been evaluated
    /// yet, and were due no earlier than <paramref name="notBefore"/> — older ones are given up on.
    /// </summary>
    Task<List<CountryChallengePost>> ReadPostsDueForEvaluationAsync(
        DateOnly today,
        DateOnly notBefore,
        CancellationToken cancellationToken = default);

    /// <summary>Tracked posts that want results and have not been evaluated yet, whether due or not.</summary>
    Task<List<CountryChallengePost>> ReadPendingPostsAsync(CancellationToken cancellationToken = default);

    void AddAwards(IEnumerable<CountryChallengePointAward> awards);

    Task<List<CountryChallengePointAward>> ReadAwardsAsync(string season, CancellationToken cancellationToken = default);

    /// <summary>Replaces every imported award of <paramref name="season"/> with <paramref name="awards"/>.</summary>
    Task ReplaceImportedAwardsAsync(
        string season,
        IEnumerable<CountryChallengePointAward> awards,
        CancellationToken cancellationToken = default);

    Task<bool> IsLeaderboardPostedOnAsync(DateOnly date, CancellationToken cancellationToken = default);

    void AddLeaderboardPost(CountryChallengeLeaderboardPost post);

    void RemoveLeaderboardPost(CountryChallengeLeaderboardPost post);
}
