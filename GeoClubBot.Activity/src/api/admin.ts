import { request } from './client';
import type {
  AdminExcuseDto,
  AdminLinkRequestDto,
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

export function adminAddStrike(
  memberNickname: string,
  strikeDate: string,
): Promise<{ strikeId: string }> {
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
