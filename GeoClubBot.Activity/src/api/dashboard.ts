import { request } from './client';
import type { DashboardDto } from '../types';

/** Fetches the aggregate dashboard payload (club, viewer, leaderboard, challenges, streaks). */
export function fetchDashboard(historyDepth: number): Promise<DashboardDto> {
  return request<DashboardDto>(`/dashboard?historyDepth=${historyDepth}`);
}
