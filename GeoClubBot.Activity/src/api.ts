import { apiBase } from './config';
import type {
  AdminExcuseDto,
  AdminLinkRequestDto,
  AdminMemberStrikesDto,
  AdminRelevantStrikeDto,
  AdminStrikeDto,
  ClubStatisticsDto,
  DashboardDto,
  LastCheckTimeDto,
  LinkRequestDto,
  MeDto,
  MissionStatsDto,
  PlayerStatisticsDto,
  ProfileDto,
  ReminderStatusDto,
  ReminderUpdateResultDto,
  TodaysXpDto,
  WeekActivityDto,
} from './types';

let accessToken: string | null = null;

/** Stores the Discord access token used to authorize dashboard requests. */
export function setAccessToken(token: string | null): void {
  accessToken = token;
}

/** An API failure with the HTTP status and the ProblemDetails detail (when the body carried one). */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Authorized JSON request against the activity API. Parses ProblemDetails error bodies into
 * <see>ApiError</see> so views can show the backend's human-readable `detail`/`title`.
 */
async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }
  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${apiBase}${path}`, { ...init, headers });

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Not a ProblemDetails body — keep the generic message.
    }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

/** Fetches the viewer's own session context (identity, link status, club, admin flag). */
export function fetchMe(): Promise<MeDto> {
  return request<MeDto>('/me');
}

/** The viewer's own XP + daily-mission activity over the trailing window. */
export function fetchMyActivity(daysBack = 7): Promise<WeekActivityDto> {
  return request<WeekActivityDto>(`/me/activity?daysBack=${daysBack}`);
}

/** The viewer's GeoGuessr profile (404s while unlinked). */
export function fetchMyProfile(): Promise<ProfileDto> {
  return request<ProfileDto>('/me/profile');
}

/** Aggregated daily-mission statistics for the viewer's club. */
export function fetchMissionStats(daysBack = 30): Promise<MissionStatsDto> {
  return request<MissionStatsDto>(`/missions/stats?daysBack=${daysBack}`);
}

/** Today's XP of the viewer's club. */
export function fetchTodaysXp(): Promise<TodaysXpDto> {
  return request<TodaysXpDto>('/club/todays-xp');
}

/** The viewer's daily-mission reminder status. */
export function fetchReminder(): Promise<ReminderStatusDto> {
  return request<ReminderStatusDto>('/me/reminder');
}

/** Sets or updates the viewer's daily-mission reminder. */
export function putReminder(body: {
  localTime: string;
  timeZoneId: string | null;
  customMessage: string | null;
}): Promise<ReminderUpdateResultDto> {
  return request<ReminderUpdateResultDto>('/me/reminder', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

/** Stops the viewer's daily-mission reminder. */
export function deleteReminder(): Promise<void> {
  return request<void>('/me/reminder', { method: 'DELETE' });
}

/** Starts the account-linking flow; the response carries the viewer's one-time password. */
export function startLinkRequest(geoGuessrUserId: string): Promise<LinkRequestDto> {
  return request<LinkRequestDto>('/me/link-request', {
    method: 'POST',
    body: JSON.stringify({ geoGuessrUserId }),
  });
}

/** Cancels the viewer's own open linking request. */
export function cancelLinkRequest(): Promise<void> {
  return request<void>('/me/link-request', { method: 'DELETE' });
}

// --- Admin endpoints (server-enforced Administrator policy; 403 for regular members) ---

export function adminFetchLastCheckTime(): Promise<LastCheckTimeDto> {
  return request<LastCheckTimeDto>('/admin/last-check-time');
}

export function adminFetchStrikes(): Promise<AdminStrikeDto[]> {
  return request<AdminStrikeDto[]>('/admin/strikes');
}

export function adminFetchRelevantStrikes(): Promise<AdminRelevantStrikeDto[]> {
  return request<AdminRelevantStrikeDto[]>('/admin/strikes/relevant');
}

export function adminFetchMemberStrikes(nickname: string): Promise<AdminMemberStrikesDto> {
  return request<AdminMemberStrikesDto>(`/admin/members/${encodeURIComponent(nickname)}/strikes`);
}

export function adminFetchExcuses(nickname?: string): Promise<AdminExcuseDto[]> {
  const query = nickname ? `?nickname=${encodeURIComponent(nickname)}` : '';
  return request<AdminExcuseDto[]>(`/admin/excuses${query}`);
}

export function adminFetchMemberActivity(nickname: string, daysBack = 7): Promise<WeekActivityDto> {
  return request<WeekActivityDto>(
    `/admin/members/${encodeURIComponent(nickname)}/activity?daysBack=${daysBack}`,
  );
}

export function adminFetchMemberStatistics(nickname: string): Promise<PlayerStatisticsDto> {
  return request<PlayerStatisticsDto>(`/admin/members/${encodeURIComponent(nickname)}/statistics`);
}

export function adminFetchClubStatistics(): Promise<ClubStatisticsDto> {
  return request<ClubStatisticsDto>('/admin/club/statistics');
}

export function adminFetchLinkRequests(): Promise<AdminLinkRequestDto[]> {
  return request<AdminLinkRequestDto[]>('/admin/link-requests');
}

export function adminAddStrike(memberNickname: string, strikeDate: string): Promise<{ strikeId: string }> {
  return request<{ strikeId: string }>('/admin/strikes', {
    method: 'POST',
    body: JSON.stringify({ memberNickname, strikeDate }),
  });
}

export function adminRevokeStrike(strikeId: string): Promise<AdminStrikeDto> {
  return request<AdminStrikeDto>(`/admin/strikes/${strikeId}/revoke`, { method: 'POST' });
}

export function adminUnrevokeStrike(strikeId: string): Promise<AdminStrikeDto> {
  return request<AdminStrikeDto>(`/admin/strikes/${strikeId}/unrevoke`, { method: 'POST' });
}

export function adminAddExcuse(
  memberNickname: string,
  from: string,
  to: string,
): Promise<{ excuseId: string }> {
  return request<{ excuseId: string }>('/admin/excuses', {
    method: 'POST',
    body: JSON.stringify({ memberNickname, from, to }),
  });
}

export function adminUpdateExcuse(excuseId: string, from: string, to: string): Promise<AdminExcuseDto> {
  return request<AdminExcuseDto>(`/admin/excuses/${excuseId}`, {
    method: 'PUT',
    body: JSON.stringify({ from, to }),
  });
}

export function adminRemoveExcuse(excuseId: string): Promise<void> {
  return request<void>(`/admin/excuses/${excuseId}`, { method: 'DELETE' });
}

export function adminCompleteLinkRequest(
  discordUserId: string,
  geoGuessrUserId: string,
  oneTimePassword: string,
): Promise<{ geoGuessrUserId: string; nickname: string }> {
  return request('/admin/link-requests/complete', {
    method: 'POST',
    body: JSON.stringify({ discordUserId, geoGuessrUserId, oneTimePassword }),
  });
}

export function adminCancelLinkRequest(discordUserId: string, geoGuessrUserId: string): Promise<void> {
  return request<void>('/admin/link-requests/cancel', {
    method: 'POST',
    body: JSON.stringify({ discordUserId, geoGuessrUserId }),
  });
}

export function adminUnlinkAccounts(discordUserId: string, geoGuessrUserId: string): Promise<void> {
  return request<void>('/admin/links/unlink', {
    method: 'POST',
    body: JSON.stringify({ discordUserId, geoGuessrUserId }),
  });
}

/**
 * Fetches the activity's public runtime config — currently the Discord client id — from the backend
 * (anonymous endpoint). The id is resolved at runtime instead of baked into the bundle so the same
 * image works for any Discord application.
 */
export async function fetchConfig(): Promise<{ clientId: string }> {
  const response = await fetch(`${apiBase}/config`);

  if (!response.ok) {
    throw new Error(`Failed to load activity configuration (${response.status}).`);
  }

  return (await response.json()) as { clientId: string };
}

/** Exchanges an OAuth2 authorization code for a Discord access token (anonymous endpoint). */
export async function exchangeToken(code: string): Promise<string> {
  const response = await fetch(`${apiBase}/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  });

  if (!response.ok) {
    throw new Error(`Token exchange failed (${response.status}).`);
  }

  const data = (await response.json()) as { accessToken: string };
  return data.accessToken;
}

/** Fetches the aggregate dashboard payload, authorized with the stored access token. */
export async function fetchDashboard(historyDepth: number): Promise<DashboardDto> {
  const headers: Record<string, string> = {};
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  const response = await fetch(`${apiBase}/dashboard?historyDepth=${historyDepth}`, { headers });

  if (!response.ok) {
    throw new Error(`Dashboard request failed (${response.status}).`);
  }

  return (await response.json()) as DashboardDto;
}
