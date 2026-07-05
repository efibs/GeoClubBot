import { request } from './client';
import type {
  LinkRequestDto,
  MeDto,
  MissionStatsDto,
  ProfileDto,
  ReminderStatusDto,
  ReminderUpdateResultDto,
  TodaysXpDto,
  WeekActivityDto,
} from '../types';

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
