# GeoClubBot Club Dashboard — Discord Activity

A Vue 3 + TypeScript [Discord Activity](https://discord.com/developers/docs/activities/overview)
(an embedded web app launched from a voice channel) that shows the club's live leaderboard, current
challenge standings, and daily streaks (a day counts only when both of its club-XP awards were
earned) — a social "TV screen" members can browse together.

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

## Project structure

```
src/
  api/          HTTP layer — one shared request() helper (bearer + ProblemDetails → ApiError)
                split by area: client, auth, dashboard, member, admin (+ index barrel)
  queries/      Server state via TanStack Vue Query — one composable module per feature
                (session, dashboard, missions, profile, reminder, linking, admin) + keys.ts
  state/        Client-only UI state (leaderboard depth, member-lookup nickname) as module refs
  components/   Reusable UI: ActionButton, PanelSection, FactRow, FormField, ErrorBanner,
                LoadingSpinner/Screen, DashboardHeader, TabNav, ConfirmDialog, feature panels
  composables/  useConfirm (in-iframe replacement for window.confirm)
  views/        Route views (Overview / Missions / Me + admin/*), thin over queries + components
  styles/       Global CSS: tokens, base (reset/shell), layout utilities, the shared row primitive
                — everything else lives in components' <style scoped> blocks
  discord.ts    Embedded App SDK handshake seam (bypassed in dev/E2E)
  router.ts     vue-router (hash history); the admin guard reads the cached session
```

Data flow: components call `useXxxQuery()` / `useXxxMutation()` composables; Vue Query owns caching,
background polling (per-view `refetchInterval`) and invalidation. Mutations invalidate the affected
query so lists refresh automatically. There is no Pinia store.

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

## Testing inside a real Discord client (via tunnel)

The dev-bypass flow above never touches Discord. To see the dashboard actually embedded in a
voice channel — real OAuth handshake, real iframe framing — the frontend needs to be served as
static files by the API, and the API needs to be reachable over public HTTPS.

1. In `appsettings.Development.json`'s `DiscordActivity` section, make sure `Enabled` is `true` and
   `ClientId` / `ClientSecret` match your Discord application (already set up for the repo's
   default dev app — see [Prerequisites](#prerequisites-discord-developer-portal--one-time-manual)
   above if you're using your own).
2. Disable the bypass — create `GeoClubBot.Activity/.env.production.local` (gitignored,
   `npm run build` picks it up automatically):

   ```
   VITE_DEV_BYPASS=false
   ```

   (The Discord client id is **not** a build-time constant — the frontend fetches it at runtime from
   the backend's `GET /api/v1/activity/config`, so the same build works for any Discord application.)

3. Build the frontend and copy it into the API's `wwwroot`. Repeat this step any time the
   Activity's source changes — or just run `scripts/rebuild-activity.sh` from the repo root, which
   does the same two commands in one go:

   ```bash
   npm run build
   rm -rf ../GeoClubBot.API/wwwroot && cp -r dist ../GeoClubBot.API/wwwroot
   ```

4. Run the API: `dotnet run --project ../GeoClubBot.API` (serves the dashboard from `wwwroot` on
   `http://localhost:5194`; no restart needed after later reruns of step 3 — static files are read
   from disk per request).
5. Tunnel it over HTTPS, e.g. with a Cloudflare quick tunnel:

   ```bash
   cloudflared tunnel --url http://localhost:5194
   ```

   Quick tunnels (`--url`) mint a fresh random `*.trycloudflare.com` hostname every time you run
   them, so you'll need to update the URL Mapping below again after every restart. For a hostname
   that survives restarts, create a named tunnel instead (`cloudflared tunnel create` +
   `cloudflared tunnel route dns` + `cloudflared tunnel run`).

6. In the Developer Portal → **Activities → URL Mappings**, point `/` and `/api` at the tunnel's
   hostname.
7. Launch the Activity from a voice channel in your test server. If a code change doesn't seem to
   show up, hard-refresh / relaunch the activity — Discord's activity iframe can cache aggressively.

## Testing

```bash
npm run test:unit     # Vitest unit + component tests
npm run test:e2e      # Playwright E2E (SDK bypassed, API mocked via route interception)
npm run typecheck     # vue-tsc
npm run lint          # ESLint (typescript-eslint + eslint-plugin-vue)
npm run format        # Prettier write (format:check verifies in CI)
```

## Build & deploy

`npm run build` emits static assets to `dist/`. The API Docker image builds this in a Node stage
and copies `dist/` into the published app's `wwwroot/`; `GeoClubBot.API` then serves it (with an
`index.html` SPA fallback) whenever `DiscordActivity:Enabled` is `true`.
