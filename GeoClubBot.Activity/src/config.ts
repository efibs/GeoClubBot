/**
 * Runtime configuration derived from Vite env vars.
 *
 * `apiBase` is `/api/v1/activity` in local dev (Vite proxies it to the backend) and
 * `/.proxy/api/v1/activity` inside Discord (routed through the activity proxy).
 */
export const isBypass = import.meta.env.VITE_DEV_BYPASS === 'true';

// The Discord client id is no longer a build-time constant — it's fetched from the backend at
// runtime (see api.ts `fetchConfig`) so the shipped bundle isn't locked to one Discord application.

export const apiBase =
  import.meta.env.VITE_API_BASE ?? (isBypass ? '/api/v1/activity' : '/.proxy/api/v1/activity');

/** Auto-refresh cadence for the dashboard, in milliseconds. */
export const refreshIntervalMs = 60_000;
