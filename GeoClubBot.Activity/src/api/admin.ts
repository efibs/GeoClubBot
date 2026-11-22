import { request } from './client';
import type {
  AdminExcuseDto,
  AdminLinkRequestDto,
  AdminMemberDto,
  AdminMemberStrikesDto,
  AdminRelevantStrikeDto,
  AdminStrikeDto,
  ClubStatisticsDto,
  LastCheckTimeDto,
  PlayerStatisticsDto,
  WeekActivityDto,
} from '../types';

// Admin endpoints (server-enforced Administrator policy; 403 for regular members).

export function adminFetchLastCheckTime(): Promise<LastCheckTimeDto> {
  return request<LastCheckTimeDto>('/admin/last-check-time');
}

export function adminFetchStrikes(): Promise<AdminStrikeDto[]> {
  return request<AdminStrikeDto[]>('/admin/strikes');
}

export function adminFetchRelevantStrikes(): Promise<AdminRelevantStrikeDto[]> {
  return request<AdminRelevantStrikeDto[]>('/admin/strikes/relevant');
}

// Members are addressed by GeoGuessr user id: nicknames are neither unique nor fixed (#139).

/** Every tracked member, for the member picker. */
export function adminFetchMembers(): Promise<AdminMemberDto[]> {
  return request<AdminMemberDto[]>('/admin/members');
}

export function adminFetchMemberStrikes(userId: string): Promise<AdminMemberStrikesDto> {
  return request<AdminMemberStrikesDto>(`/admin/members/${encodeURIComponent(userId)}/strikes`);
}

export function adminFetchExcuses(userId?: string): Promise<AdminExcuseDto[]> {
  const query = userId ? `?userId=${encodeURIComponent(userId)}` : '';
  return request<AdminExcuseDto[]>(`/admin/excuses${query}`);
}

export function adminFetchMemberActivity(userId: string, daysBack = 7): Promise<WeekActivityDto> {
  return request<WeekActivityDto>(
    `/admin/members/${encodeURIComponent(userId)}/activity?daysBack=${daysBack}`,
  );
}

export function adminFetchMemberStatistics(userId: string): Promise<PlayerStatisticsDto> {
  return request<PlayerStatisticsDto>(`/admin/members/${encodeURIComponent(userId)}/statistics`);
}

export function adminFetchClubStatistics(): Promise<ClubStatisticsDto> {
  return request<ClubStatisticsDto>('/admin/club/statistics');
}

export function adminFetchLinkRequests(): Promise<AdminLinkRequestDto[]> {
  return request<AdminLinkRequestDto[]>('/admin/link-requests');
}

export function adminAddStrike(
  memberUserId: string,
  strikeDate: string,
): Promise<{ strikeId: string }> {
  return request<{ strikeId: string }>('/admin/strikes', {
    method: 'POST',
    body: JSON.stringify({ memberUserId, strikeDate }),
  });
}

export function adminRevokeStrike(strikeId: string): Promise<AdminStrikeDto> {
  return request<AdminStrikeDto>(`/admin/strikes/${strikeId}/revoke`, { method: 'POST' });
}

export function adminUnrevokeStrike(strikeId: string): Promise<AdminStrikeDto> {
  return request<AdminStrikeDto>(`/admin/strikes/${strikeId}/unrevoke`, { method: 'POST' });
}

export function adminAddExcuse(
  memberUserId: string,
  from: string,
  to: string,
): Promise<{ excuseId: string }> {
  return request<{ excuseId: string }>('/admin/excuses', {
    method: 'POST',
    body: JSON.stringify({ memberUserId, from, to }),
  });
}

export function adminUpdateExcuse(
  excuseId: string,
  from: string,
  to: string,
): Promise<AdminExcuseDto> {
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

export function adminCancelLinkRequest(
  discordUserId: string,
  geoGuessrUserId: string,
): Promise<void> {
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
