import { afterEach, describe, expect, it, vi } from 'vitest';
import { ref, type App } from 'vue';
import { ApiError, fetchMyProfile } from '../api';
import { useProfileQuery } from './profile';
import { testClient, withSetup } from '../test/withSetup';

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>();
  return { ...actual, fetchMyProfile: vi.fn(), fetchMyActivity: vi.fn() };
});

const mockedProfile = vi.mocked(fetchMyProfile);

let app: App | undefined;
afterEach(() => {
  app?.unmount();
  app = undefined;
  vi.resetAllMocks();
});

describe('useProfileQuery', () => {
  it('treats a 404 as "not linked": null data, no error', async () => {
    mockedProfile.mockRejectedValue(new ApiError('not found', 404));
    const setup = withSetup(() => useProfileQuery(ref(true)), testClient());
    app = setup.app;
    const query = setup.result;

    await vi.waitFor(() => expect(query.isFetching.value).toBe(false));

    expect(query.data.value).toBeNull();
    expect(query.error.value).toBeNull();
  });

  it('surfaces a non-404 error', async () => {
    mockedProfile.mockRejectedValue(new ApiError('boom', 500));
    const setup = withSetup(() => useProfileQuery(ref(true)), testClient());
    app = setup.app;
    const query = setup.result;

    await vi.waitFor(() => expect(query.isError.value).toBe(true));

    expect(query.error.value).toBeInstanceOf(ApiError);
  });
});
