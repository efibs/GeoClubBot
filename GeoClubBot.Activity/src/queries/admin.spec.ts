import { afterEach, describe, expect, it, vi } from 'vitest';
import { type App } from 'vue';
import {
  ApiError,
  adminAddStrike,
  adminFetchMemberActivity,
  adminFetchMemberStatistics,
  adminFetchMemberStrikes,
  adminFetchRelevantStrikes,
  adminFetchStrikes,
} from '../api';
import { useAdminMemberLookup, useAdminStrikes } from './admin';
import { resetUiState } from '../state/ui';
import { testClient, withSetup } from '../test/withSetup';

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>();
  return {
    ...actual,
    adminFetchStrikes: vi.fn(),
    adminFetchRelevantStrikes: vi.fn(),
    adminAddStrike: vi.fn(),
    adminRevokeStrike: vi.fn(),
    adminUnrevokeStrike: vi.fn(),
    adminFetchMemberStrikes: vi.fn(),
    adminFetchMemberActivity: vi.fn(),
    adminFetchMemberStatistics: vi.fn(),
  };
});

const mockedStrikes = vi.mocked(adminFetchStrikes);
const mockedRelevant = vi.mocked(adminFetchRelevantStrikes);
const mockedAddStrike = vi.mocked(adminAddStrike);
const mockedMemberStrikes = vi.mocked(adminFetchMemberStrikes);
const mockedMemberActivity = vi.mocked(adminFetchMemberActivity);
const mockedMemberStats = vi.mocked(adminFetchMemberStatistics);

let app: App | undefined;
afterEach(() => {
  app?.unmount();
  app = undefined;
  vi.resetAllMocks();
  resetUiState();
});

function mountStrikes() {
  const setup = withSetup(() => useAdminStrikes(), testClient());
  app = setup.app;
  return setup.result;
}

describe('useAdminStrikes', () => {
  it('reloads the strikes list after a successful write', async () => {
    mockedAddStrike.mockResolvedValue({ strikeId: 's1' });
    mockedStrikes.mockResolvedValue([]);
    mockedRelevant.mockResolvedValue([]);
    const strikes = mountStrikes();
    await vi.waitFor(() => expect(strikes.isPending).toBe(false));
    mockedStrikes.mockClear();

    await strikes.add('Ada', '2026-07-04');

    expect(mockedAddStrike).toHaveBeenCalledWith('Ada', '2026-07-04');
    expect(mockedStrikes).toHaveBeenCalled();
  });

  it('keeps the error and does not reload when a write fails', async () => {
    mockedAddStrike.mockRejectedValue(new ApiError('Member not found.', 404));
    mockedStrikes.mockResolvedValue([]);
    mockedRelevant.mockResolvedValue([]);
    const strikes = mountStrikes();
    await vi.waitFor(() => expect(strikes.isPending).toBe(false));
    mockedStrikes.mockClear();

    await expect(strikes.add('Ghost', '2026-07-04')).rejects.toThrow();

    expect(strikes.error).toBe('Member not found.');
    expect(mockedStrikes).not.toHaveBeenCalled();
  });

  it('marks the section busy while a write is in flight and clears it afterwards', async () => {
    let resolveAdd: (value: { strikeId: string }) => void = () => {};
    mockedAddStrike.mockReturnValue(new Promise((resolve) => (resolveAdd = resolve)));
    mockedStrikes.mockResolvedValue([]);
    mockedRelevant.mockResolvedValue([]);
    const strikes = mountStrikes();
    await vi.waitFor(() => expect(strikes.isPending).toBe(false));

    const pending = strikes.add('Ada', '2026-07-04');
    await vi.waitFor(() => expect(strikes.busy).toBe(true));

    resolveAdd({ strikeId: 's1' });
    await pending;
    expect(strikes.busy).toBe(false);
  });
});

describe('useAdminMemberLookup', () => {
  it('treats 404s in the lookup as empty sections, not errors', async () => {
    mockedMemberStrikes.mockResolvedValue({ numActiveStrikes: 0, strikes: [] });
    mockedMemberActivity.mockRejectedValue(new ApiError('no data', 404));
    mockedMemberStats.mockRejectedValue(new ApiError('no data', 404));
    const setup = withSetup(() => useAdminMemberLookup(), testClient());
    app = setup.app;
    const lookup = setup.result;

    lookup.search('Ada');
    await vi.waitFor(() => expect(lookup.data).toBeTruthy());

    expect(lookup.data?.strikes).not.toBeNull();
    expect(lookup.data?.activity).toBeNull();
    expect(lookup.error).toBeNull();
  });
});
