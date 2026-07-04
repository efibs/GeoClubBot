import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useProfileStore } from './profile';
import { ApiError, fetchMyActivity, fetchMyProfile } from '../api';
import type { ProfileDto, WeekActivityDto } from '../types';

vi.mock('../api', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api')>();
  return {
    ApiError: original.ApiError,
    fetchMyProfile: vi.fn(),
    fetchMyActivity: vi.fn(),
  };
});

const mockedProfile = vi.mocked(fetchMyProfile);
const mockedActivity = vi.mocked(fetchMyActivity);

const profile: ProfileDto = {
  nickname: 'Ada',
  countryCode: 'de',
  createdAt: '2020-05-01T00:00:00Z',
  isProUser: true,
  level: 87,
  profileUrl: '/user/gg-1',
  ranked: null,
};

const activity: WeekActivityDto = {
  totalXp: 4200,
  numDaysDone: 5,
  joinedThisWeek: false,
  joinedAt: '2024-01-01T00:00:00Z',
  days: [],
};

describe('profile store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockedProfile.mockReset();
    mockedActivity.mockReset();
  });

  it('loads profile and activity together', async () => {
    mockedProfile.mockResolvedValue(profile);
    mockedActivity.mockResolvedValue(activity);
    const store = useProfileStore();

    await store.load();

    expect(store.profile).toEqual(profile);
    expect(store.activity).toEqual(activity);
    expect(store.error).toBeNull();
  });

  it('treats a 404 as not-linked rather than an error', async () => {
    mockedProfile.mockRejectedValue(new ApiError('not linked', 404));
    mockedActivity.mockRejectedValue(new ApiError('not linked', 404));
    const store = useProfileStore();

    await store.load();

    expect(store.error).toBeNull();
    expect(store.profile).toBeNull();
  });

  it('surfaces non-404 failures', async () => {
    mockedProfile.mockRejectedValue(new ApiError('upstream down', 502));
    mockedActivity.mockResolvedValue(activity);
    const store = useProfileStore();

    await store.load();

    expect(store.error).toBe('upstream down');
    expect(store.activity).toEqual(activity);
  });
});
