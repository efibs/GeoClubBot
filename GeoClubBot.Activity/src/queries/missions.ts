import { useQuery } from '@tanstack/vue-query';
import { fetchMissionBoard, fetchTodaysXp } from '../api';
import { refreshIntervalMs } from '../config';
import { queryKeys } from './keys';

/**
 * The mission board and today's XP are two independent queries: either can succeed or fail on its
 * own, so a failure in one never blanks the other.
 */
export function useMissionBoardQuery() {
  return useQuery({
    queryKey: queryKeys.missionBoard,
    queryFn: fetchMissionBoard,
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
