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
  // Null when the viewer can't be tied to a club (unlinked, or not currently a member).
  club: ClubDto | null;
  viewer: ViewerDto | null;
  leaderboard: LeaderboardEntryDto[];
  challenges: ChallengeResultDto[];
  streaks: MissionStreakDto[];
}

// --- Session (/me) — mirrors GeoClubBot.API/DTOs/ActivityMemberDtos.cs ---

export interface LinkedAccountDto {
  geoGuessrUserId: string;
  nickname: string;
}

// The viewer's own open account-linking request; the OTP is only ever served to its owner.
export interface LinkRequestDto {
  geoGuessrUserId: string;
  oneTimePassword: string;
}

export interface MeDto {
  // Discord ids are serialized as strings (snowflakes exceed the safe JS integer range).
  discordUserId: string;
  // Cosmetic tab gating only — every admin endpoint re-checks the policy server-side.
  isAdmin: boolean;
  linked: LinkedAccountDto | null;
  club: ClubDto | null;
  openLinkRequest: LinkRequestDto | null;
}

// --- Member tabs — mirror GeoClubBot.API/DTOs/ActivityMemberDtos.cs ---

export interface DayMissionDto {
  date: string; // ISO date (yyyy-MM-dd)
  missionCompleted: boolean;
}

export interface WeekActivityDto {
  totalXp: number;
  numDaysDone: number;
  joinedThisWeek: boolean;
  joinedAt: string;
  days: DayMissionDto[];
}

export interface RankedDto {
  rating: number | null;
  divisionName: string | null;
  tier: string | null;
  peakRating: number | null;
}

export interface ProfileDto {
  nickname: string;
  countryCode: string;
  createdAt: string;
  isProUser: boolean;
  level: number | null;
  profileUrl: string;
  ranked: RankedDto | null;
}

export interface MissionKindStatsDto {
  type: string;
  gameMode: string;
  appearanceCount: number;
  appearanceDayShare: number;
  averageTargetProgress: number;
  lastAppearance: string;
  averageDayCompletionRateWhenPresent: number | null;
}

export interface MissionStatsDto {
  clubName: string | null;
  fromDay: string;
  toDay: string;
  daysWithMissionData: number;
  totalMissionAppearances: number;
  averageDayCompletionRate: number | null;
  kinds: MissionKindStatsDto[];
}

export interface TodaysXpDto {
  xp: number | null;
  clubName: string | null;
}

export interface ReminderDto {
  id: string; // GUID, identifies the reminder for removal
  timeUtc: string; // "HH:mm", UTC
  localTime: string; // "HH:mm", in timeZoneId (or UTC when it's null)
  timeZoneId: string | null;
  customMessage: string | null;
}

export interface AddReminderResultDto {
  reminder: ReminderDto;
  // False usually means the viewer has DMs from server members disabled.
  dmDelivered: boolean;
  dmErrorCode: string | null;
}

// --- Admin area — mirror GeoClubBot.API/DTOs/ActivityAdminDtos.cs (admin endpoints only) ---

export interface LastCheckTimeDto {
  lastCheckTime: string | null;
}

export interface AdminStrikeDto {
  strikeId: string;
  nickname: string;
  timestamp: string;
  revoked: boolean;
  expiresAt: string;
}

export interface AdminRelevantStrikeDto {
  nickname: string;
  numActiveStrikes: number;
}

export interface AdminMemberStrikesDto {
  numActiveStrikes: number;
  strikes: AdminStrikeDto[];
}

export interface AdminExcuseDto {
  excuseId: string;
  nickname: string;
  from: string;
  to: string;
}

// Deliberately has no oneTimePassword — the OTP reaches admins via GeoGuessr DM only.
export interface AdminLinkRequestDto {
  discordUserId: string;
  geoGuessrUserId: string;
}

export interface PlayerStatisticsDto {
  nickname: string;
  historySince: string;
  numHistoryEntries: number;
  averagePoints: number;
  minPoints: number;
  firstQuartilePoints: number;
  medianPoints: number;
  thirdQuartilePoints: number;
  maxPoints: number;
}

export interface ClubStatisticsDto {
  clubName: string;
  averageAveragePoints: number;
  minAveragePoints: number;
  firstQuartileAveragePoints: number;
  medianAveragePoints: number;
  thirdQuartileAveragePoints: number;
  maxAveragePoints: number;
}
