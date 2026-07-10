import { computed, type Ref } from 'vue';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import { fetchDashboard } from '../api';
import { refreshIntervalMs } from '../config';
import { queryKeys } from './keys';

/**
 * Aggregate dashboard payload for the given leaderboard depth. The reactive key refetches when the
 * depth changes; `keepPreviousData` keeps the current rows on screen during that switch instead of
 * flashing empty. Polls in the background while the Overview view is mounted.
 */
export function useDashboardQuery(historyDepth: Ref<number>) {
  return useQuery({
    queryKey: computed(() => queryKeys.dashboard(historyDepth.value)),
    queryFn: () => fetchDashboard(historyDepth.value),
    refetchInterval: refreshIntervalMs,
    placeholderData: keepPreviousData,
  });
}
