using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record CalculateAverageXpQuery(Guid ClubId, int HistoryDepth) : IQuery<List<ClubMemberAverageXp>>;

public sealed class CalculateAverageXpHandler(
    IHistoryRepository history,
    IExcusesRepository excuses) : IRequestHandler<CalculateAverageXpQuery, List<ClubMemberAverageXp>>
{
    public async Task<List<ClubMemberAverageXp>> Handle(CalculateAverageXpQuery request, CancellationToken cancellationToken)
    {
        var historyEntries = await history
            .ReadHistoryEntryProjectionsByClubIdAsync(request.ClubId, cancellationToken)
            .ConfigureAwait(false);

        var allExcuses = await excuses.ReadExcuseProjectionsAsync(cancellationToken).ConfigureAwait(false);

        var excusesByUser = allExcuses
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var entriesByUser = historyEntries
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Timestamp).ToList());

        var results = new List<ClubMemberAverageXp>();

        foreach (var (userId, entries) in entriesByUser)
        {
            if (entries.Count < 2)
                continue;

            excusesByUser.TryGetValue(userId, out var userExcuses);
            userExcuses ??= [];

            var validDifferences = new List<int>();
            for (var i = 0; i < entries.Count - 1 && validDifferences.Count < request.HistoryDepth; i++)
            {
                var newer = entries[i];
                var older = entries[i + 1];

                var wasExcused = userExcuses.Any(excuse =>
                    excuse.From < newer.Timestamp && excuse.To > older.Timestamp);

                if (wasExcused)
                    continue;

                validDifferences.Add(newer.Xp - older.Xp);
            }

            if (validDifferences.Count < request.HistoryDepth)
                continue;

            var average = validDifferences.Average();

            var nickname = entries[0].MemberNickname ?? userId;
            var joinedAt = entries[0].MemberJoinedAt ?? DateTimeOffset.MaxValue;

            results.Add(new ClubMemberAverageXp(nickname, average, joinedAt));
        }

        return results;
    }
}
