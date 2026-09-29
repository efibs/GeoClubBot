import { request } from './client';
import type {
  AddReminderResultDto,
  LinkRequestDto,
  MeDto,
  MissionBoardDto,
  ProfileDto,
  ReminderDto,
  TodaysXpDto,
  WeekActivityDto,
} from '../types';

/** Fetches the viewer's own session context (identity, link status, club, admin flag). */
export function fetchMe(): Promise<MeDto> {
  return request<MeDto>('/me');
}

/** The viewer's own XP, streak and club missions over the trailing window. */
export function fetchMyActivity(daysBack = 7): Promise<WeekActivityDto> {
  return request<WeekActivityDto>(`/me/activity?daysBack=${daysBack}`);
}

/** The viewer's activity since the last weekly check, with progress towards the club's requirements. */
export function fetchMyCurrentActivity(): Promise<WeekActivityDto> {
  return request<WeekActivityDto>('/me/activity/current');
}

/** The viewer's GeoGuessr profile (404s while unlinked). */
export function fetchMyProfile(): Promise<ProfileDto> {
  return request<ProfileDto>('/me/profile');
}

/** The running mission board of the viewer's club (404 when it can't be read). */
export function fetchMissionBoard(): Promise<MissionBoardDto> {
  return request<MissionBoardDto>('/club/board');
}

/** Today's XP of the viewer's club. */
export function fetchTodaysXp(): Promise<TodaysXpDto> {
  return request<TodaysXpDto>('/club/todays-xp');
}

/** The viewer's daily reminders, ordered by time (empty when none are set). */
export function fetchReminders(): Promise<ReminderDto[]> {
  return request<ReminderDto[]>('/me/reminders');
}

/** Adds a daily reminder (or updates the one at the same time). */
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

/** Removes one of the viewer's daily reminders. */
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
