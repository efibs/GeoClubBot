import { request } from './client';

/**
 * Fetches the activity's public runtime config — currently the Discord client id — from the backend
 * (anonymous endpoint). The id is resolved at runtime instead of baked into the bundle so the same
 * image works for any Discord application.
 */
export function fetchConfig(): Promise<{ clientId: string }> {
  return request<{ clientId: string }>('/config');
}

/** Exchanges an OAuth2 authorization code for a Discord access token (anonymous endpoint). */
export async function exchangeToken(code: string): Promise<string> {
  const { accessToken } = await request<{ accessToken: string }>('/token', {
    method: 'POST',
    body: JSON.stringify({ code }),
  });
  return accessToken;
}
