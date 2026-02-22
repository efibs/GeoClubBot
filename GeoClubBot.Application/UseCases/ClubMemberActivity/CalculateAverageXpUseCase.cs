using Entities;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public class CalculateAverageXpUseCase(IUnitOfWork unitOfWork) : ICalculateAverageXpUseCase
{
    public async Task<List<ClubMemberAverageXp>> CalculateAverageXpAsync(Guid clubId, int historyDepth)
    {
        // Read all history entries for this club's members
        var historyEntries = await unitOfWork.History
            .ReadHistoryEntriesByClubIdAsync(clubId)
            .ConfigureAwait(false);

        // Read all excuses
        var excuses = await unitOfWork.Excuses
            .ReadExcusesAsync()
            .ConfigureAwait(false);

        // Group excuses by user id
        var excusesByUser = excuses
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group history entries by user id
        var entriesByUser = historyEntries
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Timestamp).ToList());

        var results = new List<ClubMemberAverageXp>();

        foreach (var (userId, entries) in entriesByUser)
        {
            // Need at least 2 entries to compute 1 interval
            if (entries.Count < 2)
                continue;

            // Get this user's excuses
            excusesByUser.TryGetValue(userId, out var userExcuses);
            userExcuses ??= [];

            // Collect valid intervals (differences between consecutive entries)
            var validDifferences = new List<int>();

            for (var i = 0; i < entries.Count - 1 && validDifferences.Count < historyDepth; i++)
            {
                var newer = entries[i];
                var older = entries[i + 1];

                // Check if the member was excused between these two timestamps
                var wasExcused = userExcuses.Any(excuse =>
                    excuse.From < newer.Timestamp && excuse.To > older.Timestamp);

                if (wasExcused)
                    continue;

                validDifferences.Add(newer.Xp - older.Xp);
            }

            // Skip members with fewer valid intervals than required
            if (validDifferences.Count < historyDepth)
                continue;

            var average = validDifferences.Average();

            // Resolve nickname and join date from the first entry's ClubMember navigation
            var clubMember = entries[0].ClubMember;
            var nickname = clubMember?.User?.Nickname ?? userId;
            var joinedAt = clubMember?.JoinedAt ?? DateTimeOffset.MaxValue;

            results.Add(new ClubMemberAverageXp(nickname, average, joinedAt));
        }

        return results;
    }
}
