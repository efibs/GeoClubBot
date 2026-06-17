import { apiBase } from './config';
import type { DashboardDto } from './types';

let accessToken: string | null = null;

/** Stores the Discord access token used to authorize dashboard requests. */
export function setAccessToken(token: string | null): void {
  accessToken = token;
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
