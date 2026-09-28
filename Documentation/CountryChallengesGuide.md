# Country Challenges Guide

Themed GeoGuessr challenges on fixed weekdays — *Mongolia Monday*, *Argentina Friday*, *Small Country
Sunday* — posted, scored and ranked by the bot. Once a day, at a time you choose, it:

1. **evaluates** the challenges whose results are due: reads their highscores, awards points, posts the
   results and hands out roles;
2. **posts the leaderboard** of the season, on the days you choose (weekly by default);
3. **announces the day's challenges**: creates them on GeoGuessr and posts them in one message per
   channel, optionally with a discussion thread.

Everything is configured in one hand-edited JSON file that the bot re-reads on every run, so an edit
takes effect at the next run without a restart. Admins can check the file at any time with
`/country-challenges-admin preview`.

---

## A week, as the bot sees it

With the example file (Mongolia Monday, Argentina + Indonesia Friday, Small Country Sunday), results one
day later and the leaderboard on Mondays:

| Day | What is posted |
|---|---|
| Monday | Results of Sunday's challenge · **leaderboard** · Mongolia Monday |
| Tuesday | Results of Mongolia Monday |
| Friday | Argentina Friday and Indonesia Friday, in one message |
| Saturday | Results of both Friday challenges, in one message |
| Sunday | Small Country Sunday (a country from the pool) |

Monday is the default leaderboard day because it is the first run at which every challenge of the
previous week has been evaluated.

---

## Switching it on

**1. appsettings** (or environment variables such as `CountryChallenges__Enabled=true`):

```jsonc
"CountryChallenges": {
  "Enabled": true,                  // the master switch — false: nothing is created or posted
  "Schedule": "0 0 17 ? * * *",     // Quartz cron: every day at 17:00
  "TimeZone": "Europe/Berlin",      // IANA id; default UTC
  "ConfigurationFilePath": "../CountryChallengesConfig.json"
}
```

- **`Schedule`** is one time for everything: results, leaderboard and the day's challenges. It must be
  present even while the feature is off, because the job scheduler reads it at start-up.
- **`TimeZone`** is the zone the schedule runs in *and* the zone that decides which day "today" is. A
  post at 00:30 Berlin time is Monday's post even though it is still Sunday in UTC, and the post time
  does not move when daylight saving starts or ends.

**2. The challenge file.** Copy [`CountryChallengesConfig.example.json`](../CountryChallengesConfig.example.json)
to wherever `ConfigurationFilePath` points and fill in your ids. In Docker, mount it into the container
the same way as `ChallengesConfig.json`. The real file is gitignored.

**3. Discord permissions.** The bot needs:
- **Send Messages** in the challenge channels;
- **Create Public Threads**, if threads are on;
- **Manage Roles**, if roles are handed out — and its own role must be above those roles;
- for role pings, either the role set to **"Allow anyone to @mention this role"**, or the bot allowed to
  **"Mention @everyone, @here and All Roles"**.

**4. Check it.** Run `/country-challenges-admin preview` for a few weekdays (see
[Checking the file](#checking-the-file)).

**5. Import the standings you kept by hand**, if any — see [Importing standings](#importing-standings).

---

## The challenge file

A value set lower down overrides the one above it:

```
built-in default  →  top of the file  →  the challenge  →  one country of a pool (Settings only)
```

Only `ChannelId` and the challenges are required; everything else has a default.

```jsonc
{
  "ChannelId": 123456789,

  "Settings": { "TimeLimit": 120, "ForbidMoving": false, "ForbidRotating": false, "ForbidZooming": false },

  "Announcement": {
    "Message": "{{mentions}}\n# :earth_africa: {{day}}'s country challenges\n{{challenges}}",
    "Entry": "### {{flag}} {{name}}\n**{{country}}** · {{settings}}\n{{link}}",
    "MentionRoleIds": [ 222 ],
    "Thread": { "Enabled": true, "Name": "{{names}} · {{date:dd.MM.}}", "AutoArchive": "OneDay" }
  },

  "Results": {
    "Enabled": true, "AfterDays": 1, "Post": true, "Top": 10,
    "Message": "# :trophy: Country challenge results\n{{results}}",
    "Entry": "### {{flag}} {{name}} · {{country}}\n{{ranking}}",
    "Points": [ 3, 2, 1 ], "RoleIds": [], "MentionRoleIds": []
  },

  "Leaderboard": {
    "Enabled": true, "Season": "Season 1", "Days": [ "Monday" ], "Top": 15,
    "Message": "# :bar_chart: Country challenge leaderboard · {{season}}\n{{leaderboard}}",
    "RoleIds": [], "MentionRoleIds": []
  },

  "Challenges": [
    { "Name": "Mongolia Monday", "Days": [ "Monday" ],
      "Country": { "Name": "Mongolia", "Code": "MN", "MapId": "…", "MapName": "An Improved Mongolia" } },

    { "Name": "Argentina Friday", "Days": [ "Friday" ],
      "Country": { "Name": "Argentina", "Code": "AR", "MapId": "…" },
      "Settings": { "ForbidMoving": true, "ForbidRotating": true, "ForbidZooming": true } },

    { "Name": "Small Country Sunday", "Days": [ "Sunday" ],
      "Entry": "### {{flag}} Small Country Sunday\nThis week: **{{country}}**!\n{{link}}",
      "Results": { "Points": [ 5, 3, 1 ], "RoleIds": [ 333 ] },
      "Pool": [
        { "Name": "Andorra", "Code": "AD", "MapId": "…" },
        { "Name": "Malta", "Code": "MT", "MapId": "…", "Settings": { "TimeLimit": 60 } } ] }
  ]
}
```

The file may contain `//` and `/* */` comments and trailing commas. Property names ignore case, and ids
may be written as strings. A property the bot does not know is an **error** that names its line — a
typo such as `"Dayz"` must not silently fall back to a default.

### Top of the file

| Setting | Default | Meaning |
|---|---|---|
| `ChannelId` | — | Default channel for announcements, results and the leaderboard. |
| `Settings` | moving, no limit | GeoGuessr settings, see below. |
| `Announcement.Message` | see above | The day's message. `{{challenges}}` becomes one `Entry` per challenge. |
| `Announcement.Entry` | see above | One challenge's part of the message. Overridable per challenge. |
| `Announcement.MentionRoleIds` | `[]` | Roles `{{mentions}}` pings. Overridable per challenge. |
| `Announcement.Thread.Enabled` | `false` | Open a thread on the day's message. |
| `Announcement.Thread.Name` | `{{names}} · {{date}}` | Thread name, cut to Discord's 100 characters. |
| `Announcement.Thread.AutoArchive` | `OneDay` | `OneHour`, `OneDay`, `ThreeDays` or `OneWeek`. |
| `Results.Message` | see above | The results message. `{{results}}` becomes one `Entry` per challenge. |
| `Results.MentionRoleIds` | `[]` | Roles `{{mentions}}` pings in the results message. |
| `Results.*` (everything else) | see the challenge table | Defaults for every challenge. |
| `Leaderboard.*` | see [Leaderboard](#leaderboard-and-seasons) | |

### A challenge

| Setting | Default | Meaning |
|---|---|---|
| `Name` | *required* | Unique. It is also the challenge's identity in the database — renaming a pool challenge starts its rotation afresh. |
| `Enabled` | `true` | `false` parks a challenge. Problems in a disabled challenge are only warnings, so an unfinished challenge can be parked without breaking the rest. |
| `Days` | — | Weekday names, e.g. `[ "Friday" ]`. |
| `Dates` | — | Specific days, e.g. `[ "2026-12-25" ]`. `Days` and/or `Dates` is required. |
| `Country` | — | The one country it is always played in. |
| `Pool` | — | Countries to rotate through. Exactly one of `Country`/`Pool`. |
| `ChannelId` | top level | Post this challenge somewhere else. |
| `MentionRoleIds`, `Entry`, `Settings` | top level | Overrides. |
| `Results.Enabled` | `true` | Evaluate this challenge at all. |
| `Results.AfterDays` | `1` | Days after posting that it is evaluated. `0` settles it at the next run, even the same day. |
| `Results.Post` | `true` | Post the results. `false` still awards points and roles. |
| `Results.ChannelId` | where it was posted | Post the results somewhere else. |
| `Results.Top` | `10` | Players listed (at most 25). |
| `Results.Entry` | see above | The challenge's part of the results message. |
| `Results.Points` | `[3, 2, 1]` | Leaderboard points for 1st, 2nd, 3rd… `[]` keeps a challenge off the leaderboard. |
| `Results.RoleIds` | `[]` | Roles for 1st, 2nd, 3rd… The same id may repeat: `[7, 7, 7]` gives role 7 to the top three. |

### A country (in `Country` or a `Pool`)

| Setting | Meaning |
|---|---|
| `Name` | Shown as `{{country}}`; unique within a pool. |
| `Code` | Two-letter ISO code, e.g. `MN`, for `{{flag}}`. Optional. |
| `MapId` | The GeoGuessr map — the part after `/maps/` in the map's link. |
| `MapName` | Shown as `{{mapName}}`; falls back to the country name. |
| `Settings` | Overrides for this country only. |

### GeoGuessr settings

`TimeLimit` is in seconds per round (`0` = no limit). `ForbidMoving`, `ForbidRotating` and
`ForbidZooming` are the usual restrictions: all three are NMPZ, only `ForbidMoving` is No Move.

### Placeholders

Using a placeholder where it is not available is an error, so a typo such as `{{contry}}` is caught
before anything is posted.

| Placeholder | Where | Becomes |
|---|---|---|
| `{{mentions}}` | messages, announcement entry | Pings of the `MentionRoleIds`. |
| `{{day}}` | everywhere | `Monday`… |
| `{{date}}`, `{{date:dd.MM.}}` | everywhere | The date, with an optional .NET format. In a results entry it is the day the challenge was played. |
| `{{names}}` | announcement message, thread name | "Argentina Friday & Indonesia Friday". |
| `{{challenges}}` | announcement message | The entries. |
| `{{name}}` `{{country}}` `{{flag}}` `{{link}}` | entries | Challenge, country, flag emoji, challenge link. |
| `{{mapName}}` `{{mapLink}}` | entries | The map's name and link. |
| `{{settings}}` `{{mode}}` `{{timeLimit}}` | entries | "NMPZ · 1 min", "NMPZ", "1 min". |
| `{{results}}` | results message | The results entries. |
| `{{ranking}}` | results entry | The medal list with points, or "No one participated". |
| `{{season}}` `{{leaderboard}}` | leaderboard message | The season and its ranking. |

### Pings

A message may ping exactly what its templates say:
- `<@&roleId>`, `<@userId>`, `@everyone` and `@here` written into a template;
- the `MentionRoleIds`, through `{{mentions}}`.

Nothing else pings — not a challenge name, and not a GeoGuessr nickname in the results, even one that
looks like a mention. A message that only reports challenges GeoGuessr refused to create pings no one.

---

## Pools

A pool plays every country once, in a random order, before any comes round again. The last country of
one round never opens the next. A country added mid-round is played before the round ends; a country
removed is simply never picked again. The rotation is worked out from the posted history, so nothing
else needs storing and nothing breaks when the pool changes.

`/country-challenges-admin preview` for the pool's weekday lists the countries left in the current round.

---

## Results, points and roles

A challenge is evaluated at the first run on or after `AfterDays` days (the due day is fixed when the
challenge is posted). Evaluating means:

- read the highscores — only players who finished all five rounds, as in the daily challenge;
- award `Points` by place to the current season;
- post the results, one message per channel for all challenges evaluated in the run;
- hand out `RoleIds`.

**Roles** are taken from everyone who holds them and given to the new places. Challenges evaluated in
the same run that use the *same* roles share them: both Friday winners get the first-place role, and a
player who placed in both keeps only their best role. Give each challenge its own roles if a role
should belong to one challenge only (it then changes hands a week later). Only players whose Discord
account is linked to GeoGuessr can receive a role.

If GeoGuessr cannot deliver the highscores, the challenge stays pending and is retried at every run
for a week, then given up.

---

## Leaderboard and seasons

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Post the leaderboard. Points are counted either way. |
| `Season` | `"Season 1"` | Changing it starts a fresh leaderboard. Older seasons stay in the database. |
| `Days`, `Dates` | `[ "Monday" ]` | When it is posted. |
| `ChannelId` | top level | Where. |
| `Top` | `15` | Places shown; players tied on the last place are all shown. |
| `Message`, `MentionRoleIds` | | As above. |
| `RoleIds` | `[]` | Roles for rank 1, 2, 3… Tied players share their rank's role. |

Ranks are shared on a tie: 9, 7, 7, 1 points rank 1, 2, 2, 4. An empty leaderboard is not posted.
Anyone can see the current standings, and their own place, with `/country-challenges leaderboard`.

---

## Importing standings

Before the bot, the leaderboard was kept by hand. `/country-challenges-admin import-standings` opens a
form: paste one player per line, **GeoGuessr nickname** (or profile link, or user id) then points:

```
1. Fibs 12
TheMapGuy: 9
https://www.geoguessr.com/user/5f1b2c3d4e5f6a7b8c9d0e1f 7
```

- A leading `1.` or `1)`, a `:` or `-` before the points, and a trailing `pts` are all fine. Blank lines
  and lines starting with `#` are skipped.
- Nicknames are matched, ignoring case, against the players the bot knows: club members and linked
  accounts. For anyone else, use their profile link.
- **All or nothing.** If a single line names no player, or several players with the same nickname,
  nothing is imported and every problem is listed with its line number.
- **Importing again replaces** the previous import of the season, so a corrected list can simply be
  imported again. Points the bot awarded itself are never touched.
- The points go into the current `Season`.

---

## Commands

| Command | Who | What |
|---|---|---|
| `/country-challenges leaderboard` | everyone | The season's standings and your own place. |
| `/country-challenges-admin preview [day]` | admins | Check the file and see what a run would post that day. `day`: `2026-12-24`, or a weekday (`sunday`, `sun`). |
| `/country-challenges-admin post-now` | admins | Run now for today. Anything already posted today is skipped. |
| `/country-challenges-admin results-now` | admins | Evaluate every challenge still waiting for its results now, even before it is due. |
| `/country-challenges-admin import-standings` | admins | Import hand-kept standings (see above). |

`post-now` is for when the bot was down at the scheduled time, or a challenge failed and should be
retried. Running it more than once a day is safe: challenges, results and the leaderboard are each
posted at most once per day.

`results-now` closes challenges early — or, while testing, without waiting a day. It evaluates every
challenge still waiting for its results exactly as the daily run would (points, results message, roles),
but leaves the leaderboard and the day's challenges to the regular run. A challenge nobody has played
yet is closed with "No one participated", so only use it once the players are done.

---

## Checking the file

`/country-challenges-admin preview` works even while the feature is switched off. It:

- lists every problem in the file with where it is, e.g.
  `Challenges[3] "Small Country Sunday" › Pool[1] "Malta": MapId is required.`;
- for a valid file, shows exactly the messages a run would post that day — results with the highscores
  as they stand now, the leaderboard including those results, and the announcement with a placeholder
  link;
- names who would be pinged, the thread, and which pool countries are left in the round.

Nothing is created, stored or posted, and the preview pings no one.

**If a run did not post anything**, the bot log (and the Discord log channel, if configured) says why.
The usual causes are a broken file (the problems are listed), the feature being off, or nothing
scheduled for the day.

---

## Testing locally

Against the mock GeoGuessr (`GeoGuessr:UseMock=true`), with `CountryChallenges:Enabled=true`, a
never-firing `Schedule`, and a leaderboard that may post any day:

1. `/country-challenges-admin post-now` — today's challenges are created in the mock and announced.
   Running it again posts nothing new: with results due the next day, nothing is settled early.
2. In the mock UI (its URL is logged at start-up), add scores to the new challenges.
3. `/country-challenges-admin results-now` — the results, points and roles, without waiting a day.
4. `post-now` — the leaderboard, now with the points.

Keep `Results.AfterDays` at 1 or more when testing this way. With `0`, results are due the day the
challenge is posted, so the very next `post-now` settles them — before anyone had a chance to add scores.

The mock issues 16-character tokens like GeoGuessr (the bot stores challenge ids in 16-character
columns), returns highscores best first, and re-creates the pending country challenges at start-up, so
scores can still be added after a restart.

---

## For developers

| Piece | Where |
|---|---|
| Job | `GeoClubBot.Infrastructure/InputAdapters/Jobs/CountryChallengeJob.cs` |
| Use cases | `GeoClubBot.Application/UseCases/CountryChallenges/` — `RunCountryChallengesCommand` sends the three phase commands |
| File model, inheritance, validation | `…/CountryChallenges/Configuration/` (`CountryChallengePlanResolver`) |
| Message rendering (shared by run and preview) | `…/CountryChallenges/Rendering/` |
| File reading | `GeoClubBot.Infrastructure/OutputAdapters/CountryChallenges/JsonFileCountryChallengeConfigurationSource.cs` |
| Persistence | `CountryChallengePosts`, `CountryChallengePointAwards`, `CountryChallengeLeaderboardPosts` via `EfCountryChallengeRepository` |
| Commands | `GeoClubBot.Discord/InputAdapters/Interactions/CountryChallenges/` |

Every phase commits before it posts: a challenge that was announced must have been stored, and points
that were announced must have been counted. A challenge that was stored but never announced is removed
again, so a later run can retry it and the pool rotation does not count it. A process-wide lock makes
the job and `post-now` wait for each other.

The job's time zone comes from `ConfiguredCronJobAttribute`'s optional second key; jobs without one
stay on UTC.
