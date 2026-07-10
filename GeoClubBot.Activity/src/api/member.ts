import { request } from './client';
import type {
  AddReminderResultDto,
  LinkRequestDto,
  MeDto,
  MissionStatsDto,
  ProfileDto,
  ReminderDto,
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

/** The viewer's daily-mission reminders, ordered by time (empty when none are set). */
export function fetchReminders(): Promise<ReminderDto[]> {
  return request<ReminderDto[]>('/me/reminders');
}

/** Adds a daily-mission reminder (or updates the one at the same time). */
export function addReminder(body: {
  localTime: string;
  timeZoneId: string | null;
  customMessage: string | null;
}): Promise<AddReminderResultDto> {
  return request<AddReminderResultDto>('/me/reminders', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Removes one of the viewer's daily-mission reminders. */
export function deleteReminder(id: string): Promise<void> {
  return request<void>(`/me/reminders/${id}`, { method: 'DELETE' });
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
