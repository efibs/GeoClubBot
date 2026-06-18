import { apiBase } from './config';
import type { DashboardDto } from './types';

let accessToken: string | null = null;

/** Stores the Discord access token used to authorize dashboard requests. */
export function setAccessToken(token: string | null): void {
  accessToken = token;
}

/**
 * Fetches the activity's public runtime config — currently the Discord client id — from the backend
 * (anonymous endpoint). The id is resolved at runtime instead of baked into the bundle so the same
 * image works for any Discord application.
 */
export async function fetchConfig(): Promise<{ clientId: string }> {
  const response = await fetch(`${apiBase}/config`);

  if (!response.ok) {
    throw new Error(`Failed to load activity configuration (${response.status}).`);
  }

  return (await response.json()) as { clientId: string };
}

/** Exchanges an OAuth2 authorization code for a Discord access token (anonymous endpoint). */
export async function exchangeToken(code: string): Promise<string> {
  const response = await fetch(`${apiBase}/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  });

  if (!response.ok) {
    throw new Error(`Token exchange failed (${response.status}).`);
  }

  const data = (await response.json()) as { accessToken: string };
  return data.accessToken;
}

/** Fetches the aggregate dashboard payload, authorized with the stored access token. */
export async function fetchDashboard(historyDepth: number): Promise<DashboardDto> {
  const headers: Record<string, string> = {};
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  const response = await fetch(`${apiBase}/dashboard?historyDepth=${historyDepth}`, { headers });

  if (!response.ok) {
    throw new Error(`Dashboard request failed (${response.status}).`);
  }

  return (await response.json()) as DashboardDto;
}
