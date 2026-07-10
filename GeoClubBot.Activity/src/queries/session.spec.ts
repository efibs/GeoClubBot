import { describe, expect, it, vi } from 'vitest';
import { useSession } from './session';
import { queryKeys } from './keys';
import { testClient, withSetup } from '../test/withSetup';
import type { MeDto } from '../types';

// The unseeded case would otherwise fire a real `/me` fetch; stub it so nothing hits the network.
vi.mock('../api', () => ({ fetchMe: vi.fn(() => Promise.reject(new Error('no session'))) }));

const baseMe: MeDto = {
  discordUserId: '42',
  isAdmin: false,
  linked: null,
  club: null,
  openLinkRequest: null,
};

function sessionFrom(me: MeDto | undefined) {
  const client = testClient();
  if (me) {
    client.setQueryData(queryKeys.session, me);
  }
  return withSetup(() => useSession(), client).result;
}

describe('useSession', () => {
  it('derives the flags from a linked admin session', () => {
    const s = sessionFrom({
      ...baseMe,
      isAdmin: true,
      linked: { geoGuessrUserId: 'g1', nickname: 'Ada' },
    });

    expect(s.isAdmin.value).toBe(true);
    expect(s.isLinked.value).toBe(true);
    expect(s.nickname.value).toBe('Ada');
  });

  it('exposes the open link request when one is present', () => {
    const request = { geoGuessrUserId: 'g1', oneTimePassword: 'otp' };
    const s = sessionFrom({ ...baseMe, openLinkRequest: request });

    expect(s.openLinkRequest.value).toEqual(request);
  });

  it('falls back to non-admin, unlinked defaults before the session loads', () => {
    const s = sessionFrom(undefined);

    expect(s.isAdmin.value).toBe(false);
    expect(s.isLinked.value).toBe(false);
    expect(s.nickname.value).toBeNull();
    expect(s.openLinkRequest.value).toBeNull();
  });
});
