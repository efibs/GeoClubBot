import { useQuery } from '@tanstack/vue-query';
import { fetchMissionStats, fetchTodaysXp } from '../api';
import { refreshIntervalMs } from '../config';
import { queryKeys } from './keys';

/**
 * Mission stats and today's XP are two independent queries (they were a `Promise.allSettled` pair
 * before): either can succeed or fail on its own, so a failure in one never blanks the other.
 */
export function useMissionStatsQuery() {
  return useQuery({
    queryKey: queryKeys.missionStats,
    queryFn: () => fetchMissionStats(),
    refetchInterval: refreshIntervalMs,
  });
}

export function useTodaysXpQuery() {
  return useQuery({
    queryKey: queryKeys.todaysXp,
    queryFn: fetchTodaysXp,
    refetchInterval: refreshIntervalMs,
  });
}
