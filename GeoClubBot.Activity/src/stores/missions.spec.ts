import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useMissionsStore } from './missions';
import { fetchMissionStats, fetchTodaysXp } from '../api';
import type { MissionStatsDto } from '../types';

vi.mock('../api', () => ({
  fetchMissionStats: vi.fn(),
  fetchTodaysXp: vi.fn(),
}));

const mockedStats = vi.mocked(fetchMissionStats);
const mockedXp = vi.mocked(fetchTodaysXp);

const stats: MissionStatsDto = {
  clubName: 'Globetrotters',
  fromDay: '2026-06-05',
  toDay: '2026-07-04',
  daysWithMissionData: 12,
  totalMissionAppearances: 36,
  averageDayCompletionRate: 0.62,
  kinds: [],
};

describe('missions store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockedStats.mockReset();
    mockedXp.mockReset();
  });

  it('loads stats and todays xp together', async () => {
    mockedStats.mockResolvedValue(stats);
    mockedXp.mockResolvedValue({ xp: 51230, clubName: 'Globetrotters' });
    const store = useMissionsStore();

    await store.load();

    expect(store.stats).toEqual(stats);
    expect(store.todaysXp?.xp).toBe(51230);
    expect(store.error).toBeNull();
    expect(store.lastUpdated).not.toBeNull();
  });

  it('keeps partial data when one call fails', async () => {
    mockedStats.mockRejectedValue(new Error('stats down'));
    mockedXp.mockResolvedValue({ xp: 100, clubName: 'Globetrotters' });
    const store = useMissionsStore();

    await store.load();

    expect(store.todaysXp?.xp).toBe(100);
    expect(store.stats).toBeNull();
    expect(store.error).toBe('stats down');
  });
});
