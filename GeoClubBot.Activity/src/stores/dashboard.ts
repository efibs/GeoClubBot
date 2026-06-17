import { defineStore } from 'pinia';
import { fetchDashboard } from '../api';
import { defaultHistoryDepth } from '../format';
import type { DashboardDto } from '../types';

interface DashboardState {
  data: DashboardDto | null;
  historyDepth: number;
  loading: boolean;
  error: string | null;
  lastUpdated: Date | null;
}

export const useDashboardStore = defineStore('dashboard', {
  state: (): DashboardState => ({
    data: null,
    historyDepth: defaultHistoryDepth,
    loading: false,
    error: null,
    lastUpdated: null,
  }),
  getters: {
    viewerNickname: (state): string | null => state.data?.viewer?.nickname ?? null,
    clubName: (state): string => state.data?.club.name ?? 'Club Dashboard',
    clubLevel: (state): number | null => state.data?.club.level ?? null,
  },
  actions: {
    /** Loads the dashboard for the current depth. The first load shows a spinner; refreshes don't. */
    async load(showSpinner = true): Promise<void> {
      if (showSpinner) {
        this.loading = true;
      }
      this.error = null;
      try {
        this.data = await fetchDashboard(this.historyDepth);
        this.lastUpdated = new Date();
      } catch (err) {
        this.error = err instanceof Error ? err.message : 'Failed to load the dashboard.';
      } finally {
        this.loading = false;
      }
    },

    /** Switches the leaderboard depth and reloads (no-op when unchanged). */
    async setHistoryDepth(depth: number): Promise<void> {
      if (depth === this.historyDepth) {
        return;
      }
      this.historyDepth = depth;
      await this.load();
    },
  },
});
