/**
 * Runtime configuration derived from Vite env vars.
 *
 * `apiBase` is `/api/v1/activity` in local dev (Vite proxies it to the backend) and
 * `/.proxy/api/v1/activity` inside Discord (routed through the activity proxy).
 */
export const isBypass = import.meta.env.VITE_DEV_BYPASS === 'true';

export const discordClientId = import.meta.env.VITE_DISCORD_CLIENT_ID;

export const apiBase =
  import.meta.env.VITE_API_BASE ?? (isBypass ? '/api/v1/activity' : '/.proxy/api/v1/activity');

/** Auto-refresh cadence for the dashboard, in milliseconds. */
export const refreshIntervalMs = 60_000;
