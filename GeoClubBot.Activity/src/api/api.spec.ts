import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, exchangeToken, fetchDashboard, fetchMe, setAccessToken } from './index';

/** Reads a header off the `RequestInit` passed to the fetch mock (request() uses a Headers object). */
function authHeaderOf(init: RequestInit): string | null {
  return new Headers(init.headers).get('Authorization');
}

describe('api client', () => {
  beforeEach(() => setAccessToken(null));
  afterEach(() => vi.unstubAllGlobals());

  it('attaches the bearer token when one is set', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });
    vi.stubGlobal('fetch', fetchMock);
    setAccessToken('bearer-xyz');

    await fetchMe();

    const [, init] = fetchMock.mock.calls[0];
    expect(authHeaderOf(init)).toBe('Bearer bearer-xyz');
  });

  it('omits Authorization when no token is set', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });
    vi.stubGlobal('fetch', fetchMock);

    await fetchMe();

    const [, init] = fetchMock.mock.calls[0];
    expect(authHeaderOf(init)).toBeNull();
  });

  it('parses a ProblemDetails body into an ApiError with status and detail', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => ({ detail: 'Member not found.' }),
      }),
    );

    await expect(fetchMe()).rejects.toMatchObject({
      name: 'ApiError',
      message: 'Member not found.',
      status: 404,
    });
  });

  it('falls back to a generic message when the error body is not ProblemDetails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        json: async () => {
          throw new Error('not json');
        },
      }),
    );

    const error = await fetchMe().catch((err: unknown) => err);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(500);
  });

  it('returns undefined for a 204 No Content response', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock);

    await expect(fetchMe()).resolves.toBeUndefined();
  });

  it('exchangeToken posts the code and returns the access token', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ accessToken: 'tok-123' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    const token = await exchangeToken('the-code');

    expect(token).toBe('tok-123');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/token');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ code: 'the-code' });
  });

  it('fetchDashboard requests the given history depth', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        club: { name: 'C', level: 1 },
        viewer: null,
        leaderboard: [],
        challenges: [],
        streaks: [],
      }),
    });
    vi.stubGlobal('fetch', fetchMock);

    const data = await fetchDashboard(8);

    expect(data.club?.name).toBe('C');
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('/dashboard?historyDepth=8');
  });
});
