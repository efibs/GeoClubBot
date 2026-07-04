import { defineStore } from 'pinia';
import { fetchMissionStats, fetchTodaysXp } from '../api';
import type { MissionStatsDto, TodaysXpDto } from '../types';

interface MissionsState {
  stats: MissionStatsDto | null;
  todaysXp: TodaysXpDto | null;
  loading: boolean;
  error: string | null;
  lastUpdated: Date | null;
}

export const useMissionsStore = defineStore('missions', {
  state: (): MissionsState => ({
    stats: null,
    todaysXp: null,
    loading: false,
    error: null,
    lastUpdated: null,
  }),
  actions: {
    /** Loads stats and today's XP in parallel; a partial failure keeps whatever succeeded. */
    async load(showSpinner = true): Promise<void> {
      if (showSpinner) {
        this.loading = true;
      }
      this.error = null;
      const [stats, todaysXp] = await Promise.allSettled([fetchMissionStats(), fetchTodaysXp()]);
      if (stats.status === 'fulfilled') {
        this.stats = stats.value;
      }
      if (todaysXp.status === 'fulfilled') {
        this.todaysXp = todaysXp.value;
      }

      const failure = [stats, todaysXp].find((result) => result.status === 'rejected');
      if (failure) {
        const reason = (failure as PromiseRejectedResult).reason;
        this.error = reason instanceof Error ? reason.message : 'Failed to load mission statistics.';
      } else {
        this.lastUpdated = new Date();
      }
      this.loading = false;
    },
  },
});
