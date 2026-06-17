import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useDashboardStore } from './dashboard';
import * as api from '../api';
import type { DashboardDto } from '../types';

vi.mock('../api');

const sample: DashboardDto = {
  club: { name: 'Geo Club', level: 5 },
  viewer: { nickname: 'Me' },
  leaderboard: [{ rank: 1, nickname: 'Me', averageXp: 100 }],
  challenges: [],
  streaks: [],
};

describe('dashboard store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
  });

  it('load populates data, club getters and lastUpdated', async () => {
    vi.mocked(api.fetchDashboard).mockResolvedValue(sample);

    const store = useDashboardStore();
    await store.load();

    expect(store.data).toEqual(sample);
    expect(store.clubName).toBe('Geo Club');
    expect(store.clubLevel).toBe(5);
    expect(store.viewerNickname).toBe('Me');
    expect(store.lastUpdated).not.toBeNull();
    expect(store.loading).toBe(false);
    expect(store.error).toBeNull();
  });

  it('load captures errors and leaves data untouched', async () => {
    vi.mocked(api.fetchDashboard).mockRejectedValue(new Error('boom'));

    const store = useDashboardStore();
    await store.load();

    expect(store.error).toBe('boom');
    expect(store.data).toBeNull();
    expect(store.loading).toBe(false);
  });

  it('setHistoryDepth reloads only when the depth changes', async () => {
    vi.mocked(api.fetchDashboard).mockResolvedValue(sample);

    const store = useDashboardStore();
    const initial = store.historyDepth;

    await store.setHistoryDepth(initial);
    expect(api.fetchDashboard).not.toHaveBeenCalled();

    await store.setHistoryDepth(initial + 1);
    expect(store.historyDepth).toBe(initial + 1);
    expect(api.fetchDashboard).toHaveBeenCalledWith(initial + 1);
  });
});
