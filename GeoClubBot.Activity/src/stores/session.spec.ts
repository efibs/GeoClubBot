import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useSessionStore } from './session';
import { fetchMe } from '../api';
import type { MeDto } from '../types';

vi.mock('../api', () => ({
  fetchMe: vi.fn(),
}));

const mockedFetchMe = vi.mocked(fetchMe);

const linkedAdmin: MeDto = {
  discordUserId: '42',
  isAdmin: true,
  linked: { geoGuessrUserId: 'gg-1', nickname: 'Ada' },
  club: { name: 'Globetrotters', level: 12 },
  openLinkRequest: null,
};

describe('session store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockedFetchMe.mockReset();
  });

  it('loads the session and derives the getters', async () => {
    mockedFetchMe.mockResolvedValue(linkedAdmin);
    const store = useSessionStore();

    await store.loadMe();

    expect(store.me).toEqual(linkedAdmin);
    expect(store.isAdmin).toBe(true);
    expect(store.isLinked).toBe(true);
    expect(store.nickname).toBe('Ada');
    expect(store.error).toBeNull();
  });

  it('defaults to non-admin and unlinked before loading and after a failure', async () => {
    const store = useSessionStore();

    expect(store.isAdmin).toBe(false);
    expect(store.isLinked).toBe(false);

    mockedFetchMe.mockRejectedValue(new Error('boom'));
    await store.loadMe();

    // Non-fatal: the error is surfaced but the safe defaults stay in place.
    expect(store.error).toBe('boom');
    expect(store.me).toBeNull();
    expect(store.isAdmin).toBe(false);
  });

  it('exposes the open link request when one exists', async () => {
    mockedFetchMe.mockResolvedValue({
      ...linkedAdmin,
      isAdmin: false,
      linked: null,
      club: null,
      openLinkRequest: { geoGuessrUserId: 'gg-2', oneTimePassword: 'otp-1234' },
    });
    const store = useSessionStore();

    await store.loadMe();

    expect(store.openLinkRequest?.oneTimePassword).toBe('otp-1234');
    expect(store.isLinked).toBe(false);
  });
});
