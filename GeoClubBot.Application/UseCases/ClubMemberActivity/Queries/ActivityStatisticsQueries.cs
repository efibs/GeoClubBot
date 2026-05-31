using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed class ActivityStatisticsHandlers(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IHistoryRepository history,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<ClubStatisticsQuery, ClubStatistics?>,
      IRequestHandler<PlayerStatisticsQuery, PlayerStatistics?>
{
    private readonly Guid _mainClubId = geoGuessrConfig.Value.MainClub.ClubId;

    public async Task<ClubStatistics?> Handle(ClubStatisticsQuery request, CancellationToken cancellationToken)
    {
        var club = await clubs.ReadClubByIdAsync(_mainClubId, cancellationToken).ConfigureAwait(false);
        if (club is null)
        {
            return null;
        }

        var entries = await history.ReadHistoryEntriesAsync(club.ClubId, cancellationToken).ConfigureAwait(false);

        var averagePointsEarned = entries
            .GroupBy(e => e.UserId)
            .Select(g => g.Select(e => e.Xp).ToList())
            .Select(g => g
                .Zip(g.Prepend(0), (a, b) => a - b)
                .Average())
            .Order()
            .ToList();

        if (averagePointsEarned.Count == 0)
        {
            return null;
        }

        var averagePoints = averagePointsEarned.Average();
        var minPoints = averagePointsEarned.Min();
        var firstQuartilePoints = averagePointsEarned.Skip(averagePointsEarned.Count / 4).First();
        var medianPoints = averagePointsEarned.Skip(averagePointsEarned.Count / 2).First();
        var thirdQuartilePoints = averagePointsEarned.Skip(averagePointsEarned.Count * 3 / 4).First();
        var maxPoints = averagePointsEarned.Max();

        return new ClubStatistics(club.Name, averagePoints, minPoints, firstQuartilePoints, medianPoints,
            thirdQuartilePoints, maxPoints);
    }

    public async Task<PlayerStatistics?> Handle(PlayerStatisticsQuery request, CancellationToken cancellationToken)
    {
        var clubMember = await clubMembers
            .ReadClubMemberByNicknameAsync(request.Nickname, cancellationToken)
            .ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return null;
        }

        var entries = await history
            .ReadHistoryEntriesByPlayerNicknameAsync(request.Nickname, clubMember.ClubId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (entries is null)
        {
            return null;
        }

        if (entries.Count == 0)
        {
            return new PlayerStatistics(request.Nickname, DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0);
        }

        var earliestTime = entries.Select(e => e.Timestamp).Min();

        var points = entries
            .OrderBy(e => e.Timestamp)
            .Select(e => e.Xp)
            .ToList();

        var pointDifferences = points
            .Skip(1)
            .Zip(points, (a, b) => a - b)
            .ToList();

        var numEntries = pointDifferences.Count;
        var averagePoints = pointDifferences.Average();
        var minPoints = pointDifferences.Min();
        var firstQuartilePoints = pointDifferences.Skip(pointDifferences.Count / 4).First();
        var medianPoints = pointDifferences.Skip(pointDifferences.Count / 2).First();
        var thirdQuartilePoints = pointDifferences.Skip(pointDifferences.Count * 3 / 4).First();
        var maxPoints = pointDifferences.Max();

        return new PlayerStatistics(request.Nickname, earliestTime, numEntries, averagePoints, minPoints,
            firstQuartilePoints, medianPoints, thirdQuartilePoints, maxPoints);
    }
}
