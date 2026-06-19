# GeoClubBot Club Dashboard — Discord Activity

A Vue 3 + TypeScript [Discord Activity](https://discord.com/developers/docs/activities/overview)
(an embedded web app launched from a voice channel) that shows the club's live leaderboard, current
challenge standings, and daily-mission streaks — a social "TV screen" members can browse together.

It is served by **GeoClubBot.API** (static assets from `wwwroot`) and talks to the activity
endpoints under `/api/v1/activity`.

## How it fits together

```
Discord voice channel ── launches ──▶ this Vue app (iframe on *.discordsays.com)
  │  authorize() → code                         │
  ▼                                             ▼
POST /api/v1/activity/token  ── exchanges ──▶ Discord OAuth2 (client secret)  → access token
  │                                             │
  ▼ Authorization: Bearer <token>               ▼
GET /api/v1/activity/dashboard ── aggregates ──▶ leaderboard + challenges + streaks (+ viewer)
```

Inside Discord the API is reached through the activity proxy at `/.proxy/api/...`; in local dev the
Vite dev server proxies `/api` to the backend.

## Prerequisites (Discord Developer Portal — one-time, manual)

1. Open your application → **Activities** → enable it.
2. Under **URL Mappings**, map `/` → the public HTTPS host serving the activity and `/api` → the
   same host (so `/.proxy/api/...` reaches the API controllers).
3. Add the **OAuth2 redirect** entry required by the Embedded App SDK.
4. Copy the **Client ID** and generate a **Client Secret**.
5. Expose the API over **public HTTPS** (host it, or tunnel with e.g. `cloudflared` during dev) —
   Discord can only load activities over HTTPS.

## Backend configuration

In `appsettings` (or env vars) on **GeoClubBot.API**, for the real in-Discord deployment:

```jsonc
"DiscordActivity": {
  "Enabled": true,
  "ClientId": "<application client id>",
  "ClientSecret": "<application client secret>"
}
```

(No CORS configuration is needed: in Discord the API is reached through the proxy, and in local dev
the Vite server proxies `/api` to the backend, so requests are always same-origin to the browser.)

## Local development (no Discord setup required)

The fastest way to **see the UI** with mock data — no backend, no Discord:

```bash
npm install
npm run test:e2e -- --ui      # opens Playwright UI; the dashboard renders against mock fixtures
```

To run against the **real local API + database** (still no Discord):

1. In `appsettings.Development.json`, enable the activity and set a dev user (already added to this repo):

   ```jsonc
   "DiscordActivity": { "Enabled": true, "DevUserId": 123456789012345678 }
   ```

   `DevUserId` makes the backend accept the frontend's bypass token as that Discord user **in the
   Development environment only**, skipping the Discord OAuth check. Set it to your own Discord user
   id to also see the "highlight the viewer" row (requires that account to be linked in the DB).

2. Run the API on its HTTP profile: `dotnet run --project ../GeoClubBot.API` (it already uses
   `GeoGuessr:UseMock=true` in development, so no real GeoGuessr is contacted).

3. Run the frontend with the SDK handshake bypassed:

   ```bash
   cp .env.example .env       # keep VITE_DEV_BYPASS=true
   npm run dev                # Vite on http://localhost:5173, proxies /api → http://localhost:5194
   ```

Open <http://localhost:5173>. Panels show whatever the local DB holds (seed via the mock GeoGuessr
UI for richer data); empty panels are expected on a fresh database.

## Testing

```bash
npm run test:unit     # Vitest unit + component tests
npm run test:e2e      # Playwright E2E (SDK bypassed, API mocked via route interception)
npm run typecheck     # vue-tsc
```

## Build & deploy

`npm run build` emits static assets to `dist/`. The API Docker image builds this in a Node stage
and copies `dist/` into the published app's `wwwroot/`; `GeoClubBot.API` then serves it (with an
`index.html` SPA fallback) whenever `DiscordActivity:Enabled` is `true`.
