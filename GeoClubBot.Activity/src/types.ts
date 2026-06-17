// Mirrors the API DTOs in GeoClubBot.API/DTOs/ActivityDtos.cs (serialized as camelCase).

export interface ClubDto {
  name: string;
  level: number;
}

export interface ViewerDto {
  nickname: string;
}

export interface LeaderboardEntryDto {
  rank: number;
  nickname: string;
  averageXp: number;
}

export interface ChallengePlayerDto {
  rank: number;
  nickname: string;
  totalScore: string;
  totalDistance: string;
}

export interface ChallengeResultDto {
  difficulty: string;
  players: ChallengePlayerDto[];
}

export interface MissionStreakDto {
  nickname: string;
  currentStreak: number;
  longestStreak: number;
}

export interface DashboardDto {
  club: ClubDto;
  viewer: ViewerDto | null;
  leaderboard: LeaderboardEntryDto[];
  challenges: ChallengeResultDto[];
  streaks: MissionStreakDto[];
}
