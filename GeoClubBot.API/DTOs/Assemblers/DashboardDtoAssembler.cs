using Entities;
using UseCases.UseCases.DailyMissionStatistics;

namespace GeoClubBot.DTOs.Assemblers;

public static class DashboardDtoAssembler
{
    public static DashboardDto Assemble(
        Club club,
        string? viewerNickname,
        IReadOnlyList<ClubMemberAverageXp> leaderboard,
        IReadOnlyList<ClubChallengeResult> challenges,
        IReadOnlyList<MemberMissionStreak> streaks)
    {
        var leaderboardDtos = leaderboard
            .Select((m, i) => new LeaderboardEntryDto(i + 1, m.Nickname, m.AverageXp))
            .ToList();

        var challengeDtos = challenges
            .Select(c => new ChallengeResultDto(
                c.Difficulty,
                c.Players
                    .Select((p, i) => new ChallengePlayerDto(i + 1, p.Nickname, p.TotalScore, p.TotalDistance))
                    .ToList()))
            .ToList();

        var streakDtos = streaks
            .Select(s => new MissionStreakDto(s.Nickname, s.CurrentStreak, s.LongestStreak))
            .ToList();

        return new DashboardDto(
            ClubDtoAssembler.AssembleDto(club),
            viewerNickname is null ? null : new ViewerDto(viewerNickname),
            leaderboardDtos,
            challengeDtos,
            streakDtos);
    }
}
