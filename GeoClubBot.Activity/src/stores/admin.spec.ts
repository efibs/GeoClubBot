import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useAdminStore } from './admin';
import {
  ApiError,
  adminAddStrike,
  adminFetchMemberActivity,
  adminFetchMemberStatistics,
  adminFetchMemberStrikes,
  adminFetchRelevantStrikes,
  adminFetchStrikes,
} from '../api';

vi.mock('../api', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api')>();
  return {
    ApiError: original.ApiError,
    adminFetchLastCheckTime: vi.fn(),
    adminFetchStrikes: vi.fn(),
    adminFetchRelevantStrikes: vi.fn(),
    adminFetchMemberStrikes: vi.fn(),
    adminFetchExcuses: vi.fn(),
    adminFetchMemberActivity: vi.fn(),
    adminFetchMemberStatistics: vi.fn(),
    adminFetchClubStatistics: vi.fn(),
    adminFetchLinkRequests: vi.fn(),
    adminAddStrike: vi.fn(),
    adminRevokeStrike: vi.fn(),
    adminUnrevokeStrike: vi.fn(),
    adminAddExcuse: vi.fn(),
    adminUpdateExcuse: vi.fn(),
    adminRemoveExcuse: vi.fn(),
    adminCompleteLinkRequest: vi.fn(),
    adminCancelLinkRequest: vi.fn(),
    adminUnlinkAccounts: vi.fn(),
  };
});

const mockedStrikes = vi.mocked(adminFetchStrikes);
const mockedRelevant = vi.mocked(adminFetchRelevantStrikes);
const mockedAddStrike = vi.mocked(adminAddStrike);
const mockedMemberStrikes = vi.mocked(adminFetchMemberStrikes);
const mockedMemberActivity = vi.mocked(adminFetchMemberActivity);
const mockedMemberStats = vi.mocked(adminFetchMemberStatistics);

describe('admin store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
  });

  it('loads strikes and relevant strikes together', async () => {
    mockedStrikes.mockResolvedValue([]);
    mockedRelevant.mockResolvedValue([{ nickname: 'Ada', numActiveStrikes: 2 }]);
    const store = useAdminStore();

    await store.loadStrikes();

    expect(store.strikes.data?.relevant).toHaveLength(1);
    expect(store.strikes.error).toBeNull();
  });

  it('reloads the strikes section after a successful write', async () => {
    mockedAddStrike.mockResolvedValue({ strikeId: 's1' });
    mockedStrikes.mockResolvedValue([]);
    mockedRelevant.mockResolvedValue([]);
    const store = useAdminStore();

    const ok = await store.addStrike('Ada', '2026-07-04');

    expect(ok).toBe(true);
    expect(mockedStrikes).toHaveBeenCalled();
  });

  it('marks the section busy while a write is in flight and clears it afterwards', async () => {
    let resolveAdd: (value: { strikeId: string }) => void = () => {};
    mockedAddStrike.mockReturnValue(new Promise((resolve) => (resolveAdd = resolve)));
    mockedStrikes.mockResolvedValue([]);
    mockedRelevant.mockResolvedValue([]);
    const store = useAdminStore();

    const pending = store.addStrike('Ada', '2026-07-04');
    expect(store.strikes.busy).toBe(true);

    resolveAdd({ strikeId: 's1' });
    await pending;
    expect(store.strikes.busy).toBe(false);
  });

  it('keeps the error and does not reload when a write fails', async () => {
    mockedAddStrike.mockRejectedValue(new ApiError('Member not found.', 404));
    const store = useAdminStore();

    const ok = await store.addStrike('Ghost', '2026-07-04');

    expect(ok).toBe(false);
    expect(store.strikes.error).toBe('Member not found.');
    expect(mockedStrikes).not.toHaveBeenCalled();
  });

  it('treats 404s in the member lookup as empty sections, not errors', async () => {
    mockedMemberStrikes.mockResolvedValue({ numActiveStrikes: 0, strikes: [] });
    mockedMemberActivity.mockRejectedValue(new ApiError('no data', 404));
    mockedMemberStats.mockRejectedValue(new ApiError('no data', 404));
    const store = useAdminStore();

    await store.lookupMember('Ada');

    expect(store.lookup.data?.strikes).not.toBeNull();
    expect(store.lookup.data?.activity).toBeNull();
    expect(store.lookup.error).toBeNull();
  });
});
