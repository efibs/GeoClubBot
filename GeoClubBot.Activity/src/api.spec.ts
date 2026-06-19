import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { exchangeToken, fetchDashboard, setAccessToken } from './api';

describe('api', () => {
  beforeEach(() => setAccessToken(null));
  afterEach(() => vi.unstubAllGlobals());

  it('exchangeToken posts the code and returns the access token', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ accessToken: 'tok-123' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    const token = await exchangeToken('the-code');

    expect(token).toBe('tok-123');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/token');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ code: 'the-code' });
  });

  it('exchangeToken throws on a non-ok response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401 }));
    await expect(exchangeToken('x')).rejects.toThrow();
  });

  it('fetchDashboard sends the bearer token and history depth', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ club: { name: 'C', level: 1 }, viewer: null, leaderboard: [], challenges: [], streaks: [] }),
    });
    vi.stubGlobal('fetch', fetchMock);
    setAccessToken('bearer-xyz');

    const data = await fetchDashboard(8);

    expect(data.club?.name).toBe('C');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/dashboard?historyDepth=8');
    expect(init.headers.Authorization).toBe('Bearer bearer-xyz');
  });

  it('fetchDashboard omits Authorization when no token is set', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) });
    vi.stubGlobal('fetch', fetchMock);

    await fetchDashboard(4);

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers.Authorization).toBeUndefined();
  });

  it('fetchDashboard throws on a non-ok response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }));
    await expect(fetchDashboard(4)).rejects.toThrow();
  });
});
