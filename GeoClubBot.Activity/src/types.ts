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

// Consecutive days on which the member played the daily challenge or a duel.
export interface DailyStreakDto {
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
  streaks: DailyStreakDto[];
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

export interface DayActivityDto {
  date: string; // ISO date (yyyy-MM-dd)
  // The daily challenge or a duel was played — the day extends the streak.
  challengeDone: boolean;
  // Board missions credited to the member that day.
  boardMissions: number;
  xp: number;
}

// One weekly requirement, e.g. "streak 4/6".
export interface RequirementDto {
  label: string;
  actual: number;
  target: number;
  met: boolean;
}

export interface WeekActivityDto {
  totalXp: number;
  // XP that counts for the club's rules; only set for the current check period.
  ruleXp: number | null;
  numChallengeDaysDone: number;
  boardMissions: number;
  boardClearBonusXp: number;
  joinedInPeriod: boolean;
  joinedAt: string;
  periodStart: string | null;
  days: DayActivityDto[];
  // Empty unless this is the current check period.
  requirements: RequirementDto[];
  // Missions of others the member pressed "help out" on — unverified, informational only.
  helpedThisWeek: number | null;
  helpedLastWeek: number | null;
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

export type BoardTileState = 'free' | 'claimed' | 'completed';

export interface BoardTileDto {
  missionId: string;
  title: string;
  currentProgress: number;
  targetProgress: number;
  state: BoardTileState;
  claimerNickname: string | null;
  claimedAt: string | null;
  helpRequested: boolean;
  helperCount: number;
  claimedByViewer: boolean;
  helpedByViewer: boolean;
}

export interface BoardDto {
  number: number;
  size: number;
  completedCount: number;
  clearedAt: string | null;
  tiles: BoardTileDto[];
}

export type ClaimState = 'ClaimAvailable' | 'HoldingOpenMission' | 'ClaimedThisCycle' | 'NoneFree';

export interface BoardViewerDto {
  claimState: ClaimState;
  claimsThisWeek: number;
  completedThisWeek: number;
  helpedThisWeek: number;
}

// The running weekly club mission board of the viewer's club.
export interface MissionBoardDto {
  clubName: string;
  periodStart: string;
  periodEnd: string;
  currentBoardNumber: number;
  allBoardsCleared: boolean;
  // When the daily claim allowance resets next.
  claimCycleEnd: string;
  boards: BoardDto[];
  viewer: BoardViewerDto | null;
}

export interface TodaysXpDto {
  xp: number | null;
  clubName: string | null;
  // Members who played the daily challenge or a duel today (UTC).
  challengeMemberCount: number | null;
  boardMissionCount: number | null;
  // Members who claimed a board mission this claim cycle; null when the board can't be read.
  claimMemberCount: number | null;
  totalMemberCount: number | null;
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
