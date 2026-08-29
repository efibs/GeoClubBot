# AI Guide — the GeoGuessr assistant

The bot can answer GeoGuessr questions from an indexed library of community guides, hold a
conversation about its answer, and show the guide images that answer a question better than prose
does. Everything here is gated behind `AI:Active`, which is **off by default**.

This document covers what it is, what it costs, how to turn it on, and what it deliberately does not do.

---

## What it looks like in Discord

- **@-mention the bot** with a question to start a conversation. You can attach a screenshot.
- **Reply to its answer** to ask a follow-up. No mention needed.
- Several people can reply to the same answer. Each reply starts an independent branch, so two people
  digging into the same answer never see each other's follow-ups.
- Answers cite the guides they used and attach the guide images they relied on.
- Each answer's footer names the model that produced it.

Admin and diagnostic commands are documented in [the command guide](../BotCommandsGuide.md#-feature-ai-assistant).

---

## Before you turn it on

Three prerequisites, in order of how badly each one bites.

### 1. Enable the Message Content intent

In the Discord Developer Portal: your application → **Bot** → **Privileged Gateway Intents** →
**Message Content Intent**.

Without it the bot cannot read the text of a reply that does not mention it, so follow-up questions
arrive empty and silently do nothing. Below 100 servers this is a checkbox with no review.

The bot only *requests* the intent when `AI:Active` is true, because requesting a privileged intent
that is not enabled in the portal drops the **entire gateway connection** with close code `4014` — the
whole bot goes offline, not just this feature. Turn the portal switch on before setting the flag.

### 2. Get an OpenRouter API key

Create one at [openrouter.ai](https://openrouter.ai). It is the only external AI dependency: it serves
both chat and embeddings.

### 3. Understand the free-tier allowance

This is the single biggest operational constraint, so it is worth being blunt about.

| Lifetime credit purchased | Requests per day |
|---|---|
| under $10 | **50** |
| $10 or more | **1000** |

A question costs **two** requests (embedding the query, then generating the answer). Indexing a source
costs one or two. At 50/day the bot answers roughly 10 questions and indexes a dozen sources — enough
to evaluate, not enough to run. **A one-time $10 purchase is effectively a prerequisite for real use.**

Check which tier you are on:

```bash
curl -s https://openrouter.ai/api/v1/key -H "Authorization: Bearer $OPENROUTER_API_KEY" | jq .data.is_free_tier
```

---

## Configuration

The key goes in `AI:OpenRouter:ApiKey`. Locally that means `appsettings.Development.json`, which is
gitignored; in production it is the environment variable `AI__OpenRouter__ApiKey`. **Never
`appsettings.json` — that one is committed.**

```json
"AI": {
  "Active": true,
  "OpenRouter": { "ApiKey": "sk-or-v1-…", "DailyRequestBudget": 45 },
  "Ingestion": { "MetaLibrarySheetId": "" }
}
```

Everything else has a working default in `appsettings.json`, where each setting is commented. The ones
worth knowing:

| Setting | Default | Why you would change it |
|---|---|---|
| `AI:OpenRouter:DailyRequestBudget` | 45 | Raise to ~950 after the $10 top-up |
| `AI:OpenRouter:PreferredModelPrefixes` | `[]` | Pin a model family you trust, e.g. `["google/"]` |
| `AI:OpenRouter:BlockedModelIds` | `[]` | Exclude a model that answers badly |
| `AI:Ingestion:MaxDailyBudgetPercent` | 60 | Share of the allowance indexing may spend |
| `AI:Ingestion:MetaLibrarySheetId` | empty | Google Sheets id of a community library to sync |
| `AI:AllowedChannelIds` | `[]` (all) | Restrict which channels the bot answers in |
| `AI:ImageRelay:PublicBaseUrl` | empty | **Required for images from blocked hosts** — see below |
| `AI:Conversation:RetentionDays` | 30 | How long stored questions are kept |

`AI:Active` requires a running Qdrant (`docker compose up qdrant`) and PostgreSQL.

---

## How it works

```
 message ─► conversation context ─► embed question ─► search index ─► ask model ─► reply + images
              (reply-chain walk)         │                 │              │
                                         └── OpenRouter ───┴──────────────┘
                                                      ▲
 guide sites ─► extract ─► chunk ─► embed ─► Qdrant ──┘
              (nightly, paced)
```

### Model selection

Free models on OpenRouter appear and are retired continuously — many carry an explicit expiry date —
so no model id is pinned. A background job reads the roster every six hours and ranks the free models
by context size, distance from any announced retirement, capability headroom and release age.

Each request sends the best candidate plus the next few as a fallback chain, and OpenRouter fails over
between them server-side within a single call. The chain always ends at `openrouter/free`, a router
that picks a free model itself — so even an empty or unreachable roster still produces an answer.

A model that fails is demoted temporarily rather than blocked, so a transient outage does not
blacklist the best model.

### The knowledge index

One Qdrant collection whose name encodes the embedding model and its dimensions, so changing either
starts a fresh collection instead of mixing incomparable vectors into the existing one.

Every chunk carries a **text** vector. Image chunks carry an **image** vector as well, and the two are
searched separately and merged by reciprocal-rank fusion. That is not over-engineering — it follows
from measurement. For a real question against a real infographic:

```
question vs the image's CAPTION      0.7305
question vs the IMAGE pixels         0.1910
question vs a BLENDED caption+image  0.3153
two UNRELATED pieces of text         0.5830   ← beats the correct text-image match
```

Two consequences the design depends on. Blending text and image into one vector is dominated by the
image and loses the text. And because text-to-text similarity sits on a higher scale than
text-to-image, a single mixed search would rank every paragraph above every image regardless of
relevance — fusion compares *positions*, not scores, which is what lets an image surface at all.

The practical upshot: **an image is found through its caption**, so extractors keep images attached to
the prose that describes them.

### Conversations

A conversation is a tree of Discord reply edges, not a list. The context for a turn is the path from
that message up to the root, which is what keeps sibling branches independent.

Limits live under `AI:Conversation`. Idle time is measured from the branch's most recent message
rather than from the root, so a long discussion that is still active is never cut off mid-thread.
Trimming only ever drops the oldest turns — removing from the middle would leave a hole in the reply
chain. Images are capped separately at 2, because an image costs roughly 1800 tokens against a context
window that free models keep small.

Stored questions are personal data. Retention is bounded and swept nightly.

### The image relay

Some guide sites answer unattended clients with 403 — plonkit.net's image CDN is one. Since the AI
provider fetches image URLs **server-side**, those images are unusable no matter how relevant they
are. The relay copies them once during indexing and serves them from this bot instead.

It is off until you give it a public base URL:

```json
"AI": {
  "ImageRelay": {
    "PublicBaseUrl": "https://your-host.your-tailnet.ts.net",
    "RelayHosts": [ "plonkit.net" ]
  }
}
```

That URL **cannot be inferred**. Behind a tunnel or reverse proxy the bot only ever sees an internal
address, so it has to be told its public one. It must be reachable from the public internet, because
the fetcher is the AI provider and Discord's embed renderer — not a browser on your network.

If you expose the bot with Tailscale Funnel, this is the `https://<machine>.<tailnet>.ts.net` address
the funnel already serves; no extra routing is needed, since the funnel forwards everything to the
bot's port.

It covers two different problems with one mechanism. Images **linked** from a host that blocks us are
downloaded and re-served. Images **embedded** in a Google Doc or slide deck have no URL at all, so
their bytes are pulled straight out of the export and stored the same way — which is why documents
only contribute pictures when the relay is on.

Stored images live in `AI:ImageRelay:Directory`, backed by the `ai-images` volume in `compose.yaml`
so they survive a redeploy. Losing them would leave every stored image URL pointing at a 404 until
the library was re-indexed.

**What the endpoint deliberately does not do.** `GET /api/v1/ai/images/{hash}` is anonymous, because
neither fetcher can carry a credential. It serves *only* bytes already written to disk during
indexing — there is no path that fetches a URL on request, which would make it an open proxy into
whatever the bot's network can reach. Images are addressed purely by the SHA-256 of their content, so
there is nothing to enumerate and no caller-supplied text ever reaches a file path. Only image types
are accepted, the declared type is sniffed from the bytes rather than trusted, size is capped, and the
route is rate-limited per client IP.

Relaying is opt-in per host rather than applied to everything: copying someone's images is a bigger
imposition than linking them, and most hosts serve their own perfectly well.

---

## Filling the index

Nothing is indexed until you ask for it.

```
/ai sync-sources     # catalogue what is available
/ai ingest count:5   # index a few now
/ai status           # models, indexed chunks, source counts
/ai search "bollards yellow"   # what retrieval returns, without asking a model
```

After that a nightly job drains the queue on its own.

### What can be indexed

| Source | Notes |
|---|---|
| plonkit.net country guides | Full text **and** images. Discovered from the site's own sitemap (~157 pages). |
| imgur albums | Infographics — often the most useful artefact for a meta. Images indexed. |
| Google Docs | Text, plus embedded images when the relay is configured. |
| Google Slides | Text, speaker notes, and embedded images when the relay is configured. |
| Google Sheets | Rows grouped into blocks, each carrying the header. |
| Direct image links | Captioned from the catalogue entry. |

A community library published as a Google Sheet can be synced by setting
`AI:Ingestion:MetaLibrarySheetId`. It is empty by default: such a library is someone else's work, so
pointing at one is a deliberate choice. Of a typical library's ~900 links, roughly two thirds are
indexable; the rest are Discord links and videos, which are **recorded with a reason** rather than
dropped so `/ai sources` reports coverage honestly.

### How long a backfill takes

Indexing may spend `MaxDailyBudgetPercent` (60%) of the daily allowance, leaving the rest for
questions — otherwise the overnight job would spend everything before anyone was awake and the bot
would be mute all day.

| Allowance | Sources per night | ~590 sources |
|---|---|---|
| 45/day (free tier) | ~13 | about six weeks |
| 950/day (after $10) | ~280 | about two nights |

A run that stops early leaves untouched sources at the front of the queue, so successive runs resume
rather than repeating. Failures back off exponentially, and a source that upstream stops listing is
tombstoned rather than deleted.

---

## Known limitations

- **Images anywhere need the relay configured.** Without `AI:ImageRelay:PublicBaseUrl`, images from
  blocked hosts keep their original links (which the provider cannot fetch) and embedded document
  images are skipped entirely, so those sources are indexed text-only. An image that still cannot be
  stored costs a picture, never the source it belongs to.
- **Indexing documents costs far more bandwidth once the relay is on.** A Google Doc's text export is
  around 2 KB; the zip export carrying its images is around 1.5 MB, and a slide deck about 7 MB. The
  heavier export is only fetched when images can actually be served, and the nightly job is paced for
  it — but a full re-index moves hundreds of megabytes rather than a few.
- **Answer quality varies with whatever is free today.** Auto-selection optimises for availability,
  not quality. Use `PreferredModelPrefixes` to steer it, and the model named in each answer's footer
  to work out what to steer towards.
- **Guild channels only.** Direct messages are ignored: they bypass the channel allowlist and are an
  easy way to drain the allowance.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Replies to the bot do nothing | Message Content intent not enabled in the Developer Portal |
| Bot is entirely offline after enabling AI | Same, but the intent *was* requested — gateway close code 4014 |
| "I'm out of free AI requests for today" | Daily allowance spent; resets 00:00 UTC |
| `/ai status` shows 0 indexed chunks | Nothing indexed yet — run `/ai sync-sources` then `/ai ingest` |
| Answers ignore the guides | Check `/ai search` first: it costs one request and shows what retrieval returns |
| Guide images show as broken in Discord | `AI:ImageRelay:PublicBaseUrl` unset, wrong, or not reachable from the public internet |
| Catalog source is `None` | The model roster could not be read; the fallback router is in use |

`/ai search` is the tool to reach for. It costs a single embedding request instead of the two a full
question costs, and shows exactly what the model would have been given — which is usually the
difference between "the model is bad" and "nothing relevant was retrieved".
