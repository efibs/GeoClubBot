using Configuration;
using Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Observability;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.OutputPorts.Repositories;

namespace GeoClubBot.DependencyInjection;

/// <summary>
/// Reads a club's mission board with that club's own token: the endpoint has no club id and
/// answers for the club the token's account belongs to.
///
/// A failed read is logged and returned as null (and cached like a success), because every caller
/// can do without the board — a reminder then just leaves the mission part out — and retrying on
/// every call would hammer GeoGuessr while it is down.
/// </summary>
public partial class CachingClubMissionBoardReader(
    IGeoGuessrClientFactory clientFactory,
    IClubMemberRepository clubMembers,
    IMemoryCache cache,
    IOptions<GeoGuessrConfiguration> config,
    ILogger<CachingClubMissionBoardReader> logger) : IClubMissionBoardReader
{
    private const string CacheName = "geoguessr_mission_boards";

    public Task<ClubMissionBoardWeek?> ReadCurrentAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        ReadCachedAsync(clubId, "current",
            async (client, ct) => await client.ReadClubMissionBoardAsync(ct).ConfigureAwait(false), cancellationToken);

    public Task<ClubMissionBoardWeek?> ReadPreviousAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        ReadCachedAsync(clubId, "previous",
            (client, ct) => client.ReadPreviousClubMissionBoardAsync(ct), cancellationToken);

    private async Task<ClubMissionBoardWeek?> ReadCachedAsync(
        Guid clubId,
        string which,
        Func<IGeoGuessrClient, CancellationToken, Task<ClubMissionBoardSnapshotDto?>> read,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"GeoGuessrMissionBoard:{clubId}:{which}";

        if (cache.TryGetValue<ClubMissionBoardWeek?>(cacheKey, out var cached))
        {
            CacheMetrics.RecordHit(CacheName);
            return cached;
        }

        CacheMetrics.RecordMiss(CacheName);

        ClubMissionBoardWeek? board = null;
        try
        {
            var dto = await read(clientFactory.CreateClient(clubId), cancellationToken).ConfigureAwait(false);
            if (dto is not null)
            {
                board = ClubMissionBoardAssembler.AssembleEntity(dto);
                await WarnIfBoardBelongsToAnotherClubAsync(clubId, board, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogBoardReadFailed(ex, which, clubId);
        }

        cache.Set(cacheKey, board, config.Value.MissionBoardCacheTimeToLive);
        return board;
    }

    /// <summary>
    /// The board says nothing about which club it belongs to. If none of its claimers is a member
    /// of the club we asked for, the club's token almost certainly belongs to someone in another
    /// club — every reminder and alert for this club would then be about the wrong board.
    /// </summary>
    private async Task WarnIfBoardBelongsToAnotherClubAsync(Guid clubId, ClubMissionBoardWeek board, CancellationToken cancellationToken)
    {
        var claimers = board.AllTiles
            .Select(t => t.ClaimedBy)
            .OfType<string>()
            .Distinct()
            .ToList();

        if (claimers.Count == 0)
        {
            return;
        }

        var members = await clubMembers.ReadClubMembersByUserIdsAsync(claimers, cancellationToken).ConfigureAwait(false);
        if (!members.Values.Any(m => m.ClubId == clubId))
        {
            LogBoardOfAnotherClub(clubId, claimers.Count);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Reading the {Which} mission board of club {ClubId} failed.")]
    partial void LogBoardReadFailed(Exception ex, string which, Guid clubId);

    [LoggerMessage(LogLevel.Warning,
        "None of the {ClaimerCount} claimers on the mission board read for club {ClubId} is a member of that club. " +
        "The club's NcfaToken probably belongs to an account in another club.")]
    partial void LogBoardOfAnotherClub(Guid clubId, int claimerCount);
}
