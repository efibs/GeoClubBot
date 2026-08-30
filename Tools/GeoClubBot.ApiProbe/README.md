# GeoClubBot.ApiProbe

A small, **read-only** console tool for finding out what the GeoGuessr API actually returns.

## Why it exists

The bot talks to GeoGuessr through Refit (`GeoClubBot.Infrastructure/OutputAdapters/GeoGuessr/IGeoGuessrApi.cs`)
and typed DTOs (`GeoClubBot.Application/OutputPorts/GeoGuessr/`). Those DTOs declare only the
fields the bot already knows about, and `System.Text.Json` silently drops everything else. So when
GeoGuessr adds a field — or when we need to answer "does this endpoint tell us *why* XP was
awarded?" — the running bot is exactly the wrong place to look.

This probe issues the same requests by hand and prints the **raw JSON**, plus a **field census**
that summarises every property the payload contains.

It was written to answer one specific question: *the daily mission gives 20 club XP, and so does
playing the daily challenge or winning a duel — can the club activity feed tell them apart?*
It is deliberately general enough to answer the next such question too.

## Read-only by construction

The probe is pointed at real accounts, so it must never write. Three independent layers:

1. **No project references.** The tool references nothing else in the solution, so write-capable
   code such as `IGeoGuessrApi.CreateChallengeAsync` (`POST /v3/challenges`) is not reachable
   from here at all. (This is also why it does its own JSON handling: reusing the DTOs would hide
   the very fields it exists to find.)
2. **`ReadOnlyGuardHandler`.** Every request passes through a `DelegatingHandler` that throws
   before hitting the network if the method is not `GET`/`HEAD`, or if the request has a body.
3. **No verb in the CLI.** Every command is a hand-written GET. The `raw` escape hatch takes a
   *path*, never a method or a payload.

The `_ncfa` token is never printed: all output goes through `TokenRedactor`, which mirrors the
bot's own `GeoClubBot.Discord/Logging/LogRedactor.cs`.

## Setting up your token

The probe authenticates the same way the bot does — with a `_ncfa` session cookie.

**Getting the cookie:** log in to <https://www.geoguessr.com> in a browser, open DevTools →
*Application* (Chrome) or *Storage* (Firefox) → *Cookies* → `https://www.geoguessr.com`, and copy
the **value** of the `_ncfa` cookie. It is URL-encoded (`%2F`, `%3D` …); copy it verbatim, don't
decode it.

> Treat it like a password: it is a full session for your GeoGuessr account. It expires — if you
> get a 401/403, grab a fresh one.

**Where to put it** — either one:

```bash
export GEOGUESSR_NCFA_TOKEN='<your _ncfa cookie value>'
export GEOGUESSR_CLUB_ID='<your club guid>'      # optional, --club overrides it
```

or create `Tools/GeoClubBot.ApiProbe/appsettings.Local.json`:

```json
{
  "GeoGuessr": {
    "NcfaToken": "<your _ncfa cookie value>",
    "ClubId": "<your club guid>"
  }
}
```

That filename is already covered by the repository's `.gitignore` rule `appsettings.*.json`, so
it cannot be committed by accident. Prefer it over the environment variable if you don't want the
token in your shell history.

## Usage

```bash
dotnet run --project Tools/GeoClubBot.ApiProbe -- <command> [options]
```

| Command | Request |
|---|---|
| `activities` | `GET /v4/clubs/{club}/activities` — the club XP feed |
| `missions` | `GET /v4/missions` — today's daily missions (for the token's own account) |
| `members` | `GET /v4/clubs/{club}/members` |
| `club` | `GET /v4/clubs/{club}` |
| `user <userId>` | `GET /v3/users/{userId}` |
| `raw <path>` | `GET https://www.geoguessr.com/api<path>` — for undocumented endpoints |

| Option | Meaning |
|---|---|
| `--club <guid>` | Club to target (overrides the configured default) |
| `--limit <n>` | Page size for `activities` (default 100) |
| `--pages <n>` | Pages to follow for `activities` (default 1) |
| `--out <file>` | Also write everything to a file |
| `--no-census` | Raw JSON only, skip the field summary |

`GEOGUESSR_API_BASE_URL` overrides the host, so the probe can be aimed at a stand-in (the
solution's `GeoClubBot.MockGeoGuessr`, or a throwaway server while working on the probe itself)
instead of the live API.

## Reading the output

Two parts. The raw JSON is the ground truth; the census is what you actually read.

```
$ dotnet run --project Tools/GeoClubBot.ApiProbe -- activities --pages 3

Field census over 5 item(s)
------------------------------------------------------------------------
activityType  [String]  present: always
         2 x  "DailyMissionCompleted"
         1 x  "DailyChallengePlayed"
         1 x  "DuelWon"
         1 x  "WeeklyMissionCompleted"
xpReward  [Number]  present: always
         4 x  20
         1 x  1000

activityType  x  xpReward
------------------------------------------------------------------------
         1 x  "DailyChallengePlayed"  ->  20
         2 x  "DailyMissionCompleted"  ->  20
         1 x  "DuelWon"  ->  20
         1 x  "WeeklyMissionCompleted"  ->  1000
```

- **Census**: every field found across the items (nested objects flattened to dotted paths), how
  many items carry it, and its distinct values with counts. A field the solution's DTOs don't
  declare shows up here immediately.
- **Cross-tab**: for `activities`, every categorical field crossed against `xpReward`. Fields with
  a distinct value per item (ids, timestamps) are skipped as non-categorical. This is the table
  that answers "do two different things worth 20 XP look different in the payload?"

The census is computed over **all** items fetched across pages, so `--pages 3 --limit 100` gives a
far better sample of rare values than a single page.

### Sampling advice

The activity feed is a rolling window. To see a value, someone in the club has to have produced it
recently — so probe at a time when the behaviour you're interested in has actually happened that
day, and pull several pages.

## Known activity types

What `GET /v4/clubs/{clubId}/activities` returns, confirmed by running this probe over 999 entries
from a 35-member club spanning 2026-07-31 to 2026-08-30. Each item is
`{ userId, type, xpReward, newLevel, recordedAt, challengeToken }`.

| `type` | `xpReward` | Cap | Meaning |
|---|---|---|---|
| 1 | 20 | once per member per day | Daily mission completed |
| 2 | 1000 | once per member per day | Weekly mission completed |
| 3 | 0 | — | Club challenge played (carries `challengeToken`) |
| 4 | 20 | once per member per day | Daily challenge played **or** duel won |

Notes, all of which the bot now depends on:

- **`type` is the only thing separating 1 from 4.** Both are worth 20 XP. Before type 4 existed the
  bot could read "a 20 XP entry" as "the daily mission"; it cannot any more.
- **Type 4 first appears at `2026-08-25T00:15:41Z`** — nothing before it, then 25-26 entries a day,
  roughly the club's size. That is GeoGuessr shipping the second XP source, and it is how the two
  types were told apart: type 1 spans the whole window, type 4 only the tail.
- **GeoGuessr does not separate "daily challenge played" from "duel won"** — both are type 4. For
  the bot they are one thing, modelled as `ClubXpActivityKind.DailyChallengeOrDuel`.
- **The daily mission awards club XP once per day**, not once per mission: 829 of 829 member-days
  had exactly one type-1 entry. (`GET /v4/missions` also returned a single mission for the day.)
- `newLevel` is set inline on whichever award crossed a club level boundary, rather than being its
  own activity type.
- `limit` appears to be ignored: the response held 25 items whether or not `limit=100` was passed.
  Paging via `paginationToken` works, so `--pages` is how you widen the sample.

The bot maps these in `GeoClubBot.Domain/ClubXpActivityKind.cs`, applied by
`GeoClubBot.Application/OutputPorts/GeoGuessr/ClubActivityKindClassifier.cs`. If a new type shows
up here, add it in those two places.

## Adding an endpoint

1. Add a `case` in `Program.cs`'s command switch calling `ProbeSingleAsync("/v4/your/path", itemsProperty)`.
   `itemsProperty` names the array to run the census over; pass `null` to let it pick the first
   array-valued property (or the root, if the response is itself an array).
2. Add it to the table in `ProbeArguments.PrintUsage()` and to the table above.
3. Keep it a GET. If you ever think you need a write, that belongs in the bot behind a use case,
   not here.

For a one-off you don't need any of that — `raw /v4/whatever` already works.
