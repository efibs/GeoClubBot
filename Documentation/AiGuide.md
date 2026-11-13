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
- Answers cite the guides they used and attach the guide images they relied on, each under its
  number and heading with a line saying what it shows. Pictures the bot has copied are uploaded with
  the answer, so they show even when the bot's public address does not.
- A question about where a plant grows gets a map of where it is recorded (see
  [Tools](#tools-when-the-excerpts-are-not-enough)).
- When the guides it was given only answer part of a question, the model can search them again in
  other words, read more of a guide, or look up where a plant grows (see [Tools](#tools-when-the-excerpts-are-not-enough)).
- Each answer's footer names the model that produced it.
- **Rate an answer** with the 👍 / 👎 the bot puts on it, or right-click it → Apps →
  **👍 Good AI answer** / **👎 Bad AI answer** to add a comment. See [Feedback](#feedback).

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

A question costs **two** requests (embedding the query, then generating the answer) when the model
answers straight away. It costs more when:
- the model calls [tools](#tools-when-the-excerpts-are-not-enough): each further round is one chat
  request, plus one embedding request when it searches. That is six at most with the default two
  rounds, and 3.2 on average over the 49 rated questions replayed on 2026-10-30;
- the first models fail, or answer with something that is not an answer, and the question is retried
  (see [Model selection](#model-selection));
- the optional query planner runs (one request; off by default, see
  [How a question becomes searches](#how-a-question-becomes-searches-measured-on-the-2026-10-23-feedback)).

Every further call is claimed from the allowance before it is made, and tools are offered only while
the day can pay for a round. Indexing a source costs one or two. At 50/day the bot answers roughly 10
to 15 questions and indexes a dozen sources — enough to evaluate, not enough to run. **A one-time $10
purchase is effectively a prerequisite for real use.**

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
| `AI:ResponseTimeoutSeconds` | 90 | Longest a model may take to write one reply once the provider has started answering. One still going is cut off and demoted for a while, and other models are asked. Of 385 replayed replies that answered, the slowest ordinary one took 68 s; the two beyond that were a runaway of 63,000 tokens (145 s) and one of 180 s. Lower it to recover from a stalled model sooner (at 60 s the fallback nemotron-3.5-lightning, which needs 64–65 s, never answers); raise it to wait for slow models, keeping `AI:OverallTimeoutSeconds` well above it |
| `AI:OpenRouter:DailyRequestBudget` | 45 | Raise to ~950 after the $10 top-up |
| `AI:OpenRouter:PreferredModelPrefixes` | `["nvidia/nemotron-3-ultra", "nvidia/nemotron-3.5-super-vl"]` | Families to put first, **best first**: an earlier prefix outranks a later one. A preference orders the models that qualify; it never lets in one the size or uptime bar keeps out, and a preferred model that keeps failing still falls behind |
| `AI:OpenRouter:BlockedModelIds` | the two Inkling models | Exclude a model that answers badly or refuses the bot, or one `/ai status` lists as excluded until restart |
| `AI:OpenRouter:MinEffectiveParametersBillions` | 8 | Smallest model asked, in billions of effective parameters (√(total × active)) |
| `AI:OpenRouter:MinProviderUptimePercent` | 50 | Models whose best provider served less of the last day's requests are not asked |
| `AI:OpenRouter:HealthCheckModelCount` | 10 | How many models each catalog refresh reads provider health for (max 20, 0 turns it off) |
| `AI:Ingestion:MaxDailyBudgetPercent` | 60 | Share of the allowance indexing may spend |
| `AI:Ingestion:MaxSourcesPerRun` | 25 | Raise after the $10 top-up — a run stops here whatever the allowance |
| `AI:Ingestion:MetaLibrarySheetId` | empty | Google Sheets id of a community library to sync |
| `AI:Ingestion:SourceOverallTimeoutSeconds` | 300 | Lower only if a guide host should be given up on sooner; it bounds a whole fetch, the download included |
| `AI:Ingestion:MaxDocumentExportBytes` | 32 MB | Past this a Google Docs export is abandoned and the text-only export indexed instead |
| `AI:Ingestion:MaxDeckExportBytes` | 512 MB | Past this a slide deck's text export is indexed instead (no pictures, notes or slide links). Decks are far heavier than documents: a median of 19 MB across the library's 41 indexed decks, 339 MB for the largest. A deck is held in memory while it is read |
| `AI:Ingestion:DocumentPicturePixels` | 1600 | Longest side a Google document's pictures are downloaded at. Lower it to save disk (about 270 kB a picture at 1600) |
| `AI:Ingestion:UnavailableRecheckDays` | 30 | How long a deleted or no-longer-shared source waits before it is checked again. The second check in a row that still finds it deleted removes its chunks |
| `AI:Ingestion:MaxFailingRetriesPerRun` | 5 | How many sources whose last attempt failed one run retries. They are taken last, fewest failures first |
| `AI:Ingestion:RemovedSourceGraceDays` | 30 | Days a source can go unlisted by every catalogue before its chunks leave the index; 0 keeps them |
| `AI:CaptionSchedule` | `0 30 4 * * ?` | When a free vision model describes indexed pictures that have no words of their own. Before `IngestionSchedule`, so its share of the allowance is claimed first |
| `AI:Captions:Enabled` | true | Turn describing off without touching what is stored |
| `AI:Captions:MaxDailyBudgetPercent` | 20 | Ceiling on the shared daily counter while describing: one chat request a picture plus one embedding request a batch |
| `AI:Captions:MaxPicturesPerRun` | 60 | Pictures described per run, whatever the allowance |
| `AI:Captions:Version` | 1 | The generation of descriptions that is current. Raise it to describe everything again over the following runs |
| `AI:Captions:MaxCaptionLength` | 400 | Longest description kept, in characters |
| `AI:Captions:TimeoutSeconds` | 90 | How long one picture may take the model before the next model is asked |
| `AI:Captions:ShowToMembers` | true | Show a description, marked "AI description", under a picture whose guide says nothing of it. Off, descriptions only serve search and the model |
| `AI:Captions:MaxConsecutiveFailures` | 3 | Unusable or failed replies in a row before a run stops |
| `AI:Tools:PlantIdentification:Enabled` | true | Name the plant in a screenshot with Pl@ntNet when the question asks what plant it shows. Does nothing without a key |
| `AI:Tools:PlantIdentification:ApiKey` | empty | A free Pl@ntNet key (my.plantnet.org). **Set it in the stack**; empty leaves the feature off |
| `AI:Tools:PlantIdentification:MinConfidence` | 0.25 | Score from which the best candidate is taken for the plant and looked up in GBIF; below it the candidates are offered as guesses |
| `AI:Tools:PlantIdentification:MaxCandidates` | 3 | Candidates offered to the model |
| `AI:Tools:PlantIdentification:MaxRequestsPerDay` | 450 | This bot's identifications a day, under Pl@ntNet's 500; counted in memory |
| `AI:Tools:PlantIdentification:CacheHours` | 24 | How long an identification is kept by the screenshot's link |
| `AI:Tools:PlantIdentification:RequestTimeoutSeconds` | 15 | Per request, the screenshot's download included |
| `AI:Tools:PlantIdentification:MaxImageBytes` | 10 MB | Largest screenshot sent |
| `AI:Tools:PlantIdentification:Project` / `Language` | `all` / `en` | Pl@ntNet's flora project and the language of the common names |
| `AI:Tools:RegionMap:Enabled` | true | Attach a map of the regions an answer names. Needs the image relay |
| `AI:Tools:RegionMap:ToolEnabled` | true | Let the model ask for a shaded map itself (`region_map`) |
| `AI:Tools:RegionMap:BoundariesUrl` | Natural Earth 10m admin-1 GeoJSON | Downloaded once (about 40 MB) into `DataDirectory` |
| `AI:Tools:RegionMap:DataDirectory` | `ai-data` | Where the boundaries file is kept; delete the file to fetch it again |
| `AI:Tools:RegionMap:SimplifyToleranceDegrees` | 0.01 | How far a boundary may be straightened when read |
| `AI:Tools:RegionMap:MaxRegions` | 15 | Most regions on one map |
| `AI:Tools:RegionMap:TimeoutSeconds` | 8 | Seconds the map after an answer may take |
| `AI:Tools:RegionMap:CacheHours` | 24 | How long a drawn map is kept |
| `AI:RoundReview:Enabled` | true | `/ai review`: a played challenge's rounds with the clues that point to each |
| `AI:RoundReview:MaxRounds` / `ExcerptsPerRound` / `MaxExcerpts` | 10 / 3 / 12 | Rounds reviewed; excerpts searched per round and offered in all |
| `AI:RoundReview:HighscoresLimit` | 100 | Places of the highscores read to find the player |
| `AI:ClueOfTheDaySchedule` | `0 0 17 * * ?` | When the clue of the day is posted, in `AI:ClueOfTheDayTimeZone` (`Europe/Berlin`; empty is UTC) |
| `AI:ClueOfTheDay:Enabled` | false | Post a lesser-known clue with its picture on the schedule. Needs `TextChannelId` |
| `AI:ClueOfTheDay:TextChannelId` | empty | The channel it is posted to |
| `AI:ClueOfTheDay:NoRepeatDays` | 180 | Days before the same clue may be posted again |
| `AI:ClueOfTheDay:ExcludeMainGuideClues` | true | Leave out clues PlonkIt has a tip like |
| `AI:ClueOfTheDay:Draws` | 8 | Clues drawn per post to choose from |
| `AI:ClueOfTheDay:Heading` | `🧭 **Clue of the day** — from *{{guide}}*` | What the post opens with |
| `AI:Quiz:Enabled` | true | `/ai quiz`: a random captioned guide picture with the answer behind a spoiler |
| `AI:Quiz:Draws` | 24 | Pictures drawn per question to find one with words of its own |
| `AI:Quiz:OnlyInAllowedChannels` | true | Keep the quiz to `AI:AllowedChannelIds` when that list is set |
| `AI:AllowedChannelIds` | `[]` (all) | Restrict which channels the bot answers in |
| `AI:Retrieval:ExcerptLimit` | 10 | Guide excerpts offered per question |
| `AI:Retrieval:MaxCarriedExcerpts` | 6 | Excerpts a follow-up carries over from the answer it replies to; 0 carries none |
| `AI:Retrieval:QueryPlanner:Enabled` | `false` | Turn on to spend one more request per question on rewriting it; see [How a question becomes searches](#how-a-question-becomes-searches-measured-on-the-2026-10-23-feedback) |
| `AI:Retrieval:CountryBoost:Weight` | 2 | How strongly a named country's guides are preferred |
| `AI:Retrieval:CountryBoost:Aliases` | `{}` | Extra names for a country or region, e.g. `{ "sabah": ["Malaysia"] }` |
| `AI:Retrieval:LesserKnown:Enabled` | `true` | Off treats "a clue I don't know yet" like any other question; see [Questions asking for something new](#questions-asking-for-something-new) |
| `AI:Retrieval:LesserKnown:SameClueSimilarity` | 0.57 | How alike an excerpt and a PlonkIt tip must be to count as the same clue. Lower marks more |
| `AI:Retrieval:LesserKnown:RandomPicks` | 6 | Picture clues drawn at random for a question asking for something new that names nothing to look for; they lead the excerpts. 0 draws none |
| `AI:Tools:Enabled` | `true` | Off answers every question in one call |
| `AI:Tools:MaxToolRounds` | 2 | Rounds of tool calls per answer; the call after the last one has to answer (1–6) |
| `AI:Tools:MaxToolCallsPerRound` | 4 | Calls run per round; more are answered "not run" (1–8) |
| `AI:Tools:AnswerDeadlineSeconds` | 90 | After this many seconds from the question, no more tools are offered (10–600) |
| `AI:Tools:SpeciesRange:Enabled` | `true` | The GBIF plant-range tool (free, no key, no AI request) |
| `AI:Tools:SpeciesRange:LookUpWhereQuestions` | `true` | Look the plant up before answering when a question asks where in the world it is found, rather than waiting for the model to call the tool |
| `AI:Tools:SpeciesRange:MinRecordsInCountry` | 500 | Fewest records a plant needs in one country before its records there are offered for a question about that country |
| `AI:Tools:SpeciesRange:Map:Enabled` | `true` | Draws the plant's records as a map and attaches it. Needs the image relay |
| `AI:Tools:SpeciesRange:Map:BaseMapStyle` | `osm-bright-en` | GBIF's base-map style. This one names states and provinces; `gbif-light` is calmer and names nothing |
| `AI:Tools:SpeciesRange:Map:TimeoutSeconds` | 8 | How long an answer waits for the map before going out without it |
| `AI:ImageRelay:PublicBaseUrl` | empty | **Required for images from blocked hosts** — see below |
| `AI:ImageRelay:MaxUploadBytesPerAnswer` | 8 MiB | Bytes of relayed pictures uploaded with one answer; pictures past it are linked. Keep it below the server's upload limit (10 MiB without boosts). 0 links every picture |
| `AI:ImageRelay:MaxUploadBytesPerPicture` | 8 MiB | Largest single picture uploaded; a bigger one is linked |
| `AI:ImageRelay:ReachabilityTimeoutSeconds` | 5 | How long `/ai status` waits for `{PublicBaseUrl}/health` |
| `AI:Conversation:RetentionDays` | 30 | How long stored questions are kept |
| `AI:Feedback:Enabled` | `true` | Collect 👍/👎 on answers at all |
| `AI:Feedback:PrefillReactions` | `true` | Whether the bot puts 👍/👎 on its own answers |
| `AI:Feedback:ConfirmWithReaction` | `true` | Whether a rated answer gets a ✅ |
| `AI:Feedback:RetentionDays` | 0 (forever) | How long rated conversations are kept |
| `AI:Feedback:MaxExportRecords` | 2000 | Cap on one `/ai feedback-export` |

`AI:Active` requires a running Qdrant (`docker compose up qdrant`) and PostgreSQL.

---

## How it works

```
 message ─► conversation context ─► [plan search] ─► embed wordings ─► search index ─► ask model ─► reply + pictures
              (reply-chain walk,     (optional,       (one request)        │           │  ▲
               carried excerpts)      off by default)                      │           ▼  │
                                                                           │     tool rounds (optional):
                                                                           │     search_guides / read_guide /
                                                                           │     species_range → ask again
                                                                           ▲
 guide sites ─► extract ─► chunk ─► embed ─► Qdrant ───────────────────────┘
              (nightly, paced)
```

### Model selection

Free models on OpenRouter appear and are retired continuously — many carry an explicit expiry date —
so no model id is pinned. A background job reads the roster every six hours and ranks the free models.
The ranking was reworked against the roster of 2026-10-23. Before that, context carried the most
weight although every candidate offered 256k or more, and structured-output support (which the bot
never uses) was rewarded. The resulting chain gave two of its three slots to models that answer this
bot with 403.

**Size leads.** The roster has no size field, so parameter counts are read from the id (`550b-a55b`,
`31b`), then the name, then the description ("16B active parameters out of 280B total"). Only phrases
in which a provider states its own size are used. A mixture of experts is compared by its effective
size, √(total × active): Nemotron 3 Ultra (550B, 55B active) counts as about 174B. Asked the same
grounded question, Ultra gave the best answer of the day, while the 30B/3B Nemotron Lightning spent its
whole 4,096-token allowance reasoning and returned nothing.

| Factor | Weight |
|---|---|
| size (log scale: 8B scores nothing, 256B full marks; unknown is neutral) | 0.40 |
| distance from retirement | 0.15 |
| provider uptime | 0.10 |
| release age | 0.10 |
| context (full marks at 128k) | 0.10 |
| capability (tools 0.6 + images 0.4) | 0.10 |
| max completion tokens | 0.05 |

Models below `MinEffectiveParametersBillions` (8B) are left out. Coding-agent models are demoted by
0.5: their description says "coding agent" or "agentic coding model", or their id ends `-code` or
`-coder`. A family's small members (nano, mini, lightning, flash) are demoted by 0.2.

**Provider health.** The roster lists a model whether or not anything behind it answers. On 2026-10-23
it listed Nemotron 3 Super, whose only provider had served 0% of the day's requests. So each refresh
also reads the public `GET /api/v1/models/{author}/{slug}/endpoints`, which needs no key and no chat
allowance. It does this for the head of the text and vision rankings (`HealthCheckModelCount`, at most
20), one read at a time through the same limiter as questions but without the chat circuit breaker.
Models whose best endpoint's `uptime_last_1d` is below `MinProviderUptimePercent` (50) are left out. A
failed read counts as healthy. The size and uptime bars are both dropped when no model that could serve
the request clears them; models the question already tried do not count towards that, so a retry falls
to the router rather than to a 2B model or one that is down.

**Preferences** are ordered: each prefix is worth one whole score step more than the next one listed.
A failure costs a preferred model its preference in proportion, so one that keeps failing still drops
back. With the shipped settings, the roster of 2026-10-30 gives the text chain
`nemotron-3-ultra-550b-a55b` → `nemotron-3.5-super-vl` → `dots-3-note-preview`.

Each request names `AI:OpenRouter:ChainLength` vetted models — three by default — and OpenRouter
fails over between them server-side within a single call. OpenRouter rejects a request naming more
than three with a plain `400`, so the catalog clamps the chain to three whatever the configuration
says. The models a request names are then exactly the ones it sends, which matters below.

A model that fails is demoted temporarily rather than blocked, so a transient outage does not
blacklist the best model. OpenRouter reports only the model that finally answered, so every entry
ahead of it in the chain must have failed. Those entries are demoted too, and the next question starts
with a model that is working. Nothing is inferred from an answering id that cannot be matched to the
chain or the roster: a model reported under a slightly different name must never cost the model that
actually answered its rank.

#### The fallback router, and why it is a last resort

`openrouter/free` is a router: it picks a free model **at random** from everything that supports the
request's features, and there is no parameter to exclude any. It used to end every chain. In the
first beta-test feedback, every answer from a model that should never have been asked came through
it, after the ranked models ahead of it had failed upstream:
- a safety classifier answering "User Safety: safe", three times;
- a 2.6B model calling a Douglas fir a pine;
- a small reasoning model inventing script features in a screenshot.

The router now appears only when too few vetted models qualify to fill the chain, for example a
roster that is empty because it could not be read. It is used at most once per question.

#### Screening and the one retry

Every completion is screened before it is used. An answer is discarded when:
- its text is a safety classifier's verdict and nothing else, such as `User Safety: safe` (NVIDIA),
  `Safety: Safe` / `Categories: None` (Qwen3Guard), `safe` / `unsafe` + `S1` (Llama Guard), or
  WildGuard's `Harmful request: no`. A real answer that merely mentions road safety is never caught;
  or
- the model that gave it is one the roster says is not a chat model, or one that is blocked.

A classifier caught by its answer rather than its description is then excluded **until restart**.
`/ai status` lists it, and the log names it at Warning, as a candidate for `BlockedModelIds`.

A reply that writes a tool call out as text (`<tool_call>`, `<function=…>`, `[TOOL_CALLS]`, or nothing
but a JSON call of one of the bot's tools) is not an answer either, and is never posted
(`ToolCallMarkupDetector`, `ai.tool_call_markup`). It never marks the model unfit: it is a good model
mis-formatting, see [Tools](#tools-when-the-excerpts-are-not-enough).

Nor is a reply that is no language at all (`GarbledTextDetector`). A provider serving a broken copy of
a model returns a valid completion made of tokens from everywhere at once: on 2026-11-11
nemotron-3-ultra, the model that answers most questions, replied
`.reurs(.तuotfrac────ζ Beliaufu … ├── tomon0première WedługNuméroθ` three seconds after being asked
for a clue. Nothing failed, so it would have been posted. The sign looked for is one no language
produces: words whose letters change alphabet half-way ("तuotfrac", "WedługNuméroθ"). A reply with two
or more of them, at least one word in twenty-five, is discarded and other models are asked; the model
is **not** excluded, since the day before it had answered the same question well. A real answer quoting
a Cyrillic place name or a Greek sign is untouched: those are words of their own, and scripts written
without spaces (Chinese, Japanese, Korean, Thai, Lao, Khmer, Burmese) never count, since "JR東日本" runs
Latin letters into them as a matter of course.

Two failures look like a refused request or our own rate limit, but belong to one model. The response
body tells them apart (`OpenRouterFailureClassifier`):
- **403 "only available on agentic harnesses".** OpenRouter reserves some free models (the Inkling
  models, October 2026) for coding agents. When the error names the chain's first model, that model is
  excluded until restart and logged at Warning as a candidate for `BlockedModelIds`. The retry goes to
  other models.
- **429 from the provider's shared pool** (`metadata.limit_source = upstream_provider_shared_pool`,
  "rate-limited upstream"). The pool is shared by every free user; this is not our key's allowance.
  Only the first model is demoted, the retry goes to other models, and the resilience pipeline does not
  wait out the window for it.

A third belongs to one model and looks like nothing at all: **a completion that opens and never ends**
(`ai.model_timed_out`). OpenRouter answers a chat request at once — status and headers — and keeps the
response open with blank padding while the model works. Refit takes a response at its headers and reads
the body afterwards, so neither the client's timeout nor the pipeline's attempt timeout was still
counting: on 2026-11-11 nemotron-3-ultra stalled mid-request and the response stayed open for 57
minutes, then ended empty. In Discord that is "is typing" for an hour, with one of the
`MaxConcurrentRequests` answer slots held throughout. `ResponseBodyDeadlineHandler` now reads every
response from OpenRouter to its end inside the HTTP pipeline and gives up on a body that has not arrived
`AI:ResponseTimeoutSeconds` (90) after its headers. It sits outside the resilience pipeline on purpose: the
client's overall timeout still counts while the body is read, and the pipeline never sends the request
again, which would only wait on the same model while spending the provider's daily requests. Only the
first model is demoted — OpenRouter moves down the chain on an error, not on silence — and the question
goes to other models, slow attempt or not. A later call of a tool-using answer that stalls is replaced
by the last call without tools, asked of a chain without that model.

When the first chain fails, or its answer is discarded, the question is retried once against models
it has not tried yet. A chain whose first model gave a verdict still has two untried models, so they
stay in play. The retry claims its own request from the daily allowance. It is skipped when:
- the provider could not be reached, or rate-limited every model: all models sit behind the same
  provider and the same key;
- the provider refused the request as sent (a 4xx other than 408 or 429, such as a replayed
  screenshot whose Discord link has expired, but not the agentic-harness 403 above): it would fail the
  same way against any model. For the same reason the models are not demoted for it;
- the first attempt was slow: a struggling provider would only double the wait (a model cut off for
  not finishing is the exception, see above: the provider had answered at once);
- no untried model remains: this costs nothing, because the retry chain is read before its request is
  claimed.

**Not every free entry is a chat model.** OpenRouter lists music and image generators on the same
roster as chat models. They are billed per second of audio or per picture rather than per token, so
both `pricing.prompt` and `pricing.completion` read `"0"` and a free-price filter admits them — and
with context windows of 1M they rank near the top. They are excluded by output modality: a genuine
chat model declares `output_modalities: ["text"]` and nothing else, while a generator declares text
*and* audio or image. The rule is therefore text-only output, not text-among-others —
`google/lyria-3-pro-preview` advertises `["text", "audio"]` and would slip past a "contains text" check.

**Safety classifiers are the harder case.** A guardrail model like
`nvidia/nemotron-3.5-content-safety:free` moderates other models' traffic, and answers a question
with a verdict on it — `User Safety: safe` — instead of a reply. Structurally it is indistinguishable
from an assistant: text in, text out, free, 128k of context, so it ranks *well*. There is no category
or flag on the roster to sort it by, and the failure tracker cannot learn it either, because the
request succeeds and the completion is valid. The only signal the provider gives is its own prose, so
a model whose description says **guardrail**, **content safety** or **safeguard** is skipped. Measured
against the full 446-model roster those three terms catch all four classifiers and nothing else; the
rule is kept narrow on purpose, since a missed guardrail costs one odd answer somebody can thumbs-down
while a wrongly excluded model is silently never used again. A classifier the description misses is
caught by its answer instead (see screening above), and `AI:OpenRouter:BlockedModelIds` keeps it out
for good.

### Citations

Excerpts are numbered in the prompt, and the model cites them inline as `[2]`. An excerpt that comes
with a picture is marked `(picture)`, or `(N pictures)`, and cited the same way; citing it attaches its
pictures. A PlonkIt picture is marked "usually with a distribution map": about 70% of them carry one, an
inset drawn into the tip's picture (measured on 973 pictures from 15 guides), yet only 4 of 642 such
tips say "map" in their text. Shown only "(picture)", the model told a user asking for the distribution
"I don't have the actual distribution image" while it cited one. A bare number is useless to a reader,
so every citation in the answer is resolved back to its excerpt and listed under the reply as a named,
clickable link:

```
Roads starting with MR are exclusive to Eswatini [1], and the MR9 is lined with pine [2].

-# [1] [Eswatini > Identifying](https://www.plonkit.net/eswatini#m1jr)
-# [2] 🖼️ [Eswatini > Regional clues](https://www.plonkit.net/eswatini#1chu)
```

Masked links work here because Discord honours them in what a **bot** posts through the API; it
blocks them in messages people type, to stop a friendly label hiding a hostile URL. The number stays
outside the link so it still anchors the `[1]` in the prose.

**Numbers are the reader's, not the retriever's.** The model cites excerpts by retrieval rank, which
reads as `[6] [1] [4]`. So citations are renumbered in order of first mention, and the list under the
answer always runs 1, 2, 3. A cited picture takes its place in that sequence: it is listed with 🖼️,
attached as an embed titled with the same number, and linked to its guide. Up to
`AI:MaxImagesInReply` pictures are attached, the first of each cited excerpt first; any beyond that are
still listed. Text is numbered by link **and** section: two chunks of one section share a number, but
two sections of one Google Doc, which share one link with no anchor, are two source lines
("[2] USA › Front Plate Requirements", "[3] USA › Counties"). Before 2026-10-30 a whole Doc was one
number, and an answer listing nine states cited all of them to one "[2] … Counties (33) ->". Two
pictures from one album are two attachments with two numbers.

An excerpt can carry several pictures: identical text captioning several pictures of one slide is one
excerpt. Its pictures are folded together only when they come from the same link and section; a
picture whose text merely matches another guide's is dropped, never shown under the wrong guide.

**Each cited excerpt's pictures are a gallery of their own.** Discord draws embeds that share a link
as one embed, under the first one's title, with at most four pictures. Pictures used to be grouped by
their guide's link and titled as the group they became: five clues cited from one Google Doc stood
under "[2][3][4][5][6] 28 Secret US Metas (5 pictures)" — "it is hard to understand which image is
which clue" — and the fifth was not drawn at all (2026-11-09). Now:
- every excerpt's pictures get a link no other gallery has: the guide's, told apart by a `_=n` query
  parameter placed before the fragment, so the link still opens at the section it names;
- a gallery is titled with its number and the heading it is listed under beneath the answer
  ("[2] USA · 28 Secret US Metas › Useful metas"), never the document's name alone, and with
  "(3 pictures)" when it shows several;
- under the title stands what the guide says the pictures show (`PictureCaption`: the start of the
  excerpt's text, at most 200 characters, as plain text);
- an excerpt with more than four pictures takes a second gallery ("pictures 5–7 of 7").

Discord adds up the titles and descriptions of a message's embeds and refuses the whole message past
6,000 characters. Every embed of a gallery carries the title, so ten pictures under long headings use
a good part of that before any caption. Captions are therefore given in reading order while they fit;
one that no longer fits is left out whole, and its pictures still stand under their heading.

**Models cite in every style going, and all of them are understood**: `[2]`, `[image 2]`,
`**[image 2]**`, `【image 2】` and `【2】`, `[1, 3, 5]`, `[7-8]`, `[^2]`, `【4:0†source】`, and a citation
put in backticks. Each is rewritten as plain `[n]`.
- Parentheses or emphasis holding only citations go with them.
- A line holding only citations is dropped, since the list already says it.
- A number that was never offered is removed along with the space before it.
- Two citations joined by a dash (`[1]–[6]`, each optionally in its own emphasis) are a range, expanded
  before renumbering when ascending and spanning at most 10; otherwise only the ends count. Renumbered
  separately, `([1]–[6])` once reached a reader as `([5]–[4])`.
- The prompt's own notes copied into the answer — `(picture)`, `(N pictures)`, "usually with a
  distribution map", "(the user saw this as N)" — are removed before citations are resolved.
- Markdown links, `[2019]` and code are left alone.

The previous handling knew two styles, and deleted image markers outright. Beta testers were shown
what that left: "see the plate in ****", "(, )", and a literal "image 1" with no picture.

Answers replayed as conversation history lose their citations. Their numbers pointed at an earlier
turn's excerpts, and the new turn numbers its own from one again. What a follow-up keeps instead is the
excerpts themselves: see [Conversations](#conversations).

**Attribution does not depend on the model complying.** Free models cite inconsistently — the same
question cites on one run and not the next — so when an answer cites nothing at all, the guides it
was given are credited on its behalf:

```
-# This answer cites no guide. Closest matches:
-# [Eswatini > Identifying](https://www.plonkit.net/eswatini#m1jr)
```

These carry no numbers, because there is no marker in the prose for one to anchor. The heading says
outright that they are not backing for the answer. What the model did with the excerpts is unknown,
and a reader otherwise takes the list for sources ("I can't confirm if it is actually correct since
there is no source"). The list credits what was found for *this* question; the guides a follow-up
carries over from the previous answer are credited only when nothing else was found, or an Indonesia
follow-up would list the Malaysia guides. When retrieval found nothing, nothing is credited.

#### What the model is told

The system prompt (`AiPromptBuilder.SystemPrompt`) carries one instruction per failure testers
reported, in the first round of feedback and in the 49 rated answers of 2026-10-23:
- **One citation style**, and no source list of its own.
- **Show pictures.** For "where" and distribution questions, cite a picture that shows the distribution;
  when the user asks to see something, cite a picture of it. Never say it cannot display images.
- **It cannot see the guide pictures.** It knows only their text, so it may say what a picture is for
  but not describe it. "Describe what it shows" had it inventing their contents.
- **Lead with the direct answer**, then the specifics, without generic filler.
- **Keep the guides' qualifiers.** "Mainly" never becomes "only", and "rare in the Northeast" never
  becomes a list of nine states.
- **Say when the guides describe something else.** Asked about snake traps, it answered about possum
  guards; asked about snow bollards, it answered about road bollards.
- **Stay in the asked country**, and mention another only to rule out a look-alike. A guide's region is
  not the user's: a cactus found in a California guide turned into an answer about California.
- **A follow-up keeps the earlier turns' topic and country** ("How can I identify Sabah?" after a
  Malaysia question means within Malaysia).
- **Describe only what is visible in a user's screenshot**, judge it against the country the user
  names, and say when it does not match.
- **Licence plates are blurred** in Street View, so plate letters are rarely usable.
- **Answer the whole question, and mark what is not from the guides.** Each such claim is a sentence
  of its own starting "Not from the guides:". "Say which part is not from the guides" alone was
  ignored: on 2026-10-29 unflagged claims ("Philippine roads typically have solid white outer edge
  lines") read as the guides' own; on 2026-10-30, 9 tools-on answers used the marker.

The feedback of 2026-11-09 added four, which took the prompt from 510 to about 660 words:
- **Show a clue, and never explain it from its name.** "Ohio gap" was answered as "a telltale gap in
  pavement or road markings"; the guide says a gap in a road line at a raised pavement marker.
- **Name the plant in a screenshot yourself.** Asked what cactus a screenshot showed, the model
  answered with the Brazilian cactus of a look-alike slide: "it was South Africa", and "it did not
  mention the species". A look-alike is a candidate, and its country is not the screenshot's.
- **PlonkIt is what a player already knows**: see
  [Questions asking for something new](#questions-asking-for-something-new).
- **Practice maps.** Guides name GeoGuessr maps to practise on; their links are the one kind the
  model may write.

With tools offered, a short paragraph (`AnswerTools.SystemPrompt`) is added: see
[Tools](#tools-when-the-excerpts-are-not-enough).

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

#### How the lists are weighted, and why (measured on production)

A written question runs two searches: against chunk text, and against image pixels. A screenshot adds
a third, against image pixels. The lists are fused here (`KnowledgeHitFusion`) rather than by Qdrant,
because they must count differently and Qdrant 1.15 cannot weight them. The weights came from
replaying the beta testers' 18 written questions read-only against the production index. That's
144 top-8 slots. A slot counts as on-target when its text is about what the question asks:

| Retrieval | On-target | Picture found only by its pixels | Same text twice |
|---|---|---|---|
| Before: both lists at full weight | 73 | 53 | 21 |
| Question-against-pixels at **0.25**, copies collapsed | **88** | 13 | 0 |
| Question-against-pixels left out | 97 | 0 | 0 |

- **Pixel-only pictures are weighted to a quarter.** A picture found only by its pixels has a caption
  unrelated to the question, or it would have ranked by text, so the model can say nothing about it.
  At full weight such pictures took over a third of the slots. On "where can I find Khasi pines?"
  three pictures of Ugandan forest pushed out the two Vietnam guides that answer the rest of the
  question, which is exactly what the tester reported as missing.
- **Not zero, because agreement still counts.** "White car long antenna" ranks only 39th by its text
  but first by its picture, and together that makes it the answer. Dropping the list would lose it.
  A screenshot's own search keeps full weight: for "where is this?" it is the signal.
- **The same text is offered once.** Two authors' copies of one guide, or one paragraph captioning
  several pictures, took up to three of eight slots. When one copy carries a picture, that copy is
  the one kept, so the model can still attach it.
- **Searches are exact, not approximate.** The approximate index missed the chunk that answers "is
  pampa's grass a real meta?". It scored 0.372 against a tenth place of 0.359, yet was not among the
  first 300 returned. A full scan of the 35k points costs 22 ms for both lists on the production
  server, against 9 ms, which a question that then spends seconds with the model does not notice.
  Exact search alone changed little in the top 8 overall; it removes misses like that one.

To measure a change to any of this, re-run the replay rather than reasoning about it:
`Tools/GeoClubBot.RetrievalProbe`'s `compare` counts these same columns for any feedback export, and
`replay` shows what every rated question is offered today
([README](../Tools/GeoClubBot.RetrievalProbe/README.md)).

#### How a question becomes searches (measured on the 2026-10-23 feedback)

`RetrievalQueryComposer` (`Application/UseCases/AI/Retrieval/`) turns a question into the searches
above. All of its wordings are embedded in **one** request, so none of this costs an extra request
unless the planner runs. The probe's measured table for each step is in its
[README](../Tools/GeoClubBot.RetrievalProbe/README.md).

- **Follow-ups are searched by their branch.** A follow-up is searched by the previous question joined
  to it (weight 1) and by its own words (×0.5). Every bad follow-up in the export had been searched by
  its own words alone. "What about corn?" found its tip 8th before and 1st now; the A-type guardrail
  tips went from 28th and not found to 1st and 2nd.
- **Countries are a soft boost, never a filter.** Countries are matched against the index's own
  country names by `CountryGazetteer`. It merges spellings (usa/united states, czechia/czech republic),
  adjectives, typos ("Philippenes") and regions (Sabah, Kyushu, Scandinavia). The names are read by a
  full scroll and cached for 6 h by `KnowledgeCountryDirectory`. Every text wording is searched again
  within those countries, as extra lists at ×2 in the same query batch. Five or more countries means a
  question about many places, and gets no boost. The share of excerpts from the named country went from
  72% to 94%. `/ai search country:` stays a hard filter.
  - A follow-up adds the branch's countries only when it names none itself, or refers back ("it",
    "this", "same"), and never for "which countries…" questions. A screenshot follow-up asking "where"
    adds none.
  - When the question itself names a country, the screenshot's own pixel list counts ×0.5. With a
    stand-in screenshot, "Is this bollard a meta in Australia?" went from 2 to 8 Australian excerpts.
- **Pictures whose only text is a heading** ("Svalbard — Identifying Svalbard", "Distribution") count
  ×0.25, which cut them from 29 of the offered slots to 6–9. This does not apply to screenshot
  questions.
- **Ten excerpts instead of eight** (`ExcerptLimit`).
- **The query planner** (`AI:Retrieval:QueryPlanner`, **off** by default) is one chat call to the
  answer chain's head at temperature 0.1. It returns a standalone question, up to 3 searches in the
  guides' own terms ("snow poles", not "snow bollards"), the countries the question is about, and up
  to 3 literal key phrases. It never fails a question: when its allowance share
  (`MaxDailyBudgetPercent`, 75%) is spent, or the call fails, times out or returns an unreadable reply,
  the question is searched without a plan. After a timeout it is skipped for 30 minutes.
  - With nemotron-3-ultra at the head it offered 16 of 24 named excerpts against 14 without it, at a
    4.4 s median. Behind dots-3-note-preview (34 s median) only 11 of 49 plans arrived within the 20 s
    timeout.
  - It ships off because at 45 requests a day it costs about 5 of 22 questions, and the
    [tools](#tools-when-the-excerpts-are-not-enough) now cover much the same ground. With a
    1000-a-day key and a fast head model, measure it with the probe's `compare --allow-chat` before
    turning it on.
  - Key phrases search the chunks that contain the phrase literally. Without a full-text index that is
    a case-sensitive substring match, so their effect is small.

#### Questions asking for something new

"Tell me a reliable clue I don't know yet", "a good USA clue that is not on PlonkIt". To a player,
"a clue I don't know" means one PlonkIt does not carry: it is the guide everybody has read. Asked
exactly that on 2026-11-09, the bot was offered nine PlonkIt tips of ten, and answered three times
running with clues PlonkIt has: Sikkim's metal poles, then two Ukrainian car metas ("Both Ukraine car
metas are again on PlonkIt and I knew them very well"). The model is shown ten excerpts, not a guide,
so it could not have known.

- **`LesserKnownQuestion`** reads such a question by its shape ("not on PlonkIt", "I don't know yet",
  "lesser-known", "obscure", "niche", "secret" …), and a follow-up keeps asking it: "give me another
  one, maybe outside India" counts when one of the three questions before it on its branch did.
- **PlonkIt's own tips are left out** of the search (`KnowledgeQuery.ExcludedSourceType`), and of the
  model's tool searches unless it asks for `source: "plonkit"`.
- **What other guides tell that PlonkIt tells too is marked** (`MainGuideOverlap`). Each excerpt is
  compared with PlonkIt's tips for its country, under every spelling of the country, by the text
  vector it already has: one read of the index, no embedding request. An excerpt at or above
  `SameClueSimilarity` reads "(also on PlonkIt)" in the prompt, and the model is told to pass those
  over. The excerpts carried from the previous answer are marked too.
- **A question that names nothing to look for gets picture clues drawn at random** (`ClueSampler`,
  `RandomPicks`). "Tell me a reliable clue I don't know yet" gives a search only its wording to go by,
  and what is worded like it is no clue: replayed on 2026-11-11 it was offered a document's legend and
  answered "the guide maps each meta by region, using dark red for where it's most common" — no clue,
  and no picture, which the member had asked for. So when every word of the question is one a request
  for a clue is made of (`LesserKnownQuestion.NamesNoSubject`: no country, no "bollard", no number, no
  screenshot), the index draws picture chunks at random from every guide but PlonkIt
  (`IKnowledgeIndex.SampleAsync`, Qdrant's own random sample). Kept are pictures with words of their
  own — not a bare heading — one per guide, and not those PlonkIt has a tip like; `RandomPicks` of them
  lead the excerpts, and what the search found fills the places left. The same question then got
  raised lane-line tabs in Florida and Texas, with their picture. It costs no AI request, differs
  each time it is asked, and a failed draw only means the question is answered from its search. A
  follow-up ("another one please") is drawn for only when the questions before it named nothing
  either: after "any lesser-known bollard clues?" it is still about bollards, and is searched.

The bar was read off real pairs with the probe's `overlap` command (2026-11-10):

| Pair | Similarity |
|---|---|
| Meghalaya's taxis, in a document and on PlonkIt | 0.83 |
| Oregon's bollard ("28 Secret US Metas") | 0.69 |
| Sikkim's metal poles | 0.61 |
| Ukraine's flag car / leaf car (a slide's five-word caption) | 0.59 / 0.58 |
| Two different tips of one country | up to about 0.55, the odd one-line remark 0.61 |

At 0.57 about a ninth of one India document's chunks are marked, nearly all of them clues PlonkIt does
carry. It errs towards marking: a clue wrongly passed over costs one candidate of many.

### Tools: when the excerpts are not enough

The question is still retrieved and sent with its excerpts first. Alongside it, the model is offered
three tools (`Application/UseCases/AI/Tools/`). They used to be ruled out because requiring tool
support would exclude most free models; on 2026-10-23 all 17 free text-only, non-guardrail models
advertised `tools`.

- **`search_guides(query, country?, pictures_only?, source?)`** runs the bot's own search (the same
  lists, fusion and country boost) in the model's words. A named country is preferred, never required.
  `source` reads PlonkIt alone (`"plonkit"`, to check whether it carries a clue) or every other guide
  (`"other"`). It returns up to 6 new excerpts.
- **`read_guide(excerpt)`** reads more of the guide excerpt n came from: the same section first, then
  sibling sections, then the sections it sits in, then the rest. It returns at most 8 chunks or 6,000
  characters, and skips pictures whose only text is a heading. "Neighbours" are exact only for Google
  Docs and Slides, whose local keys are paragraph ordinals.
- **`species_range(name, country?)`** says where a plant, tree or crop is recorded, from GBIF (free, no
  key, no AI request). A scientific name is matched first, and a common name falls back to GBIF's
  vernacular names ("khasi pine" → *Pinus kesiya*). Records are counted per country, or per state or
  province when a country is given (Yucca filifera in Mexico: Nuevo León 384, Zacatecas 247 …). The
  result is one citable excerpt linking `gbif.org/species/{key}`, and its text says these are
  occurrence records, not abundance. It comes with a **map** of the records, attached when the answer
  cites it (below).
  - A common name that is no one species is read as the genus most species carrying it belong to:
    "cholla" is 49 species of *Cylindropuntia* against 16 of *Grusonia*. A genus is taken with at
    least 3 species and 60 % of those carrying the name, which leaves "pine", "bamboo" and "cactus"
    unanswered, as they should be.

**Plants are also looked up before the model answers** (`PlantRangeLookAhead`). Left to the model, the
tool was called in none of the 49 replayed answers on 2026-10-30, through two rewordings of its
description: given guides on India and Vietnam, the model took them for the answer, and "Khasi pines are
also found outside of India" stayed the rated complaint. So when a question asks where in the world
something is found — "where can I find Khasi pines?", "where does agave lechuguilla grow?", "which
countries have baobabs?" — `PlantRangeQuestion` reads the thing it asks about, and GBIF is asked while
the question is searched. A plant it can stand behind leads the excerpts found for the question. This
costs no AI request, needs no tool calling (it works with `AI:Tools:Enabled` off), and a question about
something else costs only a GBIF read or two.
- **Names are tried in the forms GBIF knows**: as written, with a binomial's epithet lower-cased
  ("Agave Lechuguilla" matches the genus *Agave* alone); in the singular ("Khasi pines" is unknown,
  "Khasi pine" is *Pinus kesiya*); and without a word like "trees". A lone word left that way is taken
  only by its scientific name: "dragon" is a common name of tarragon.
- **A question about one place is left to the model and the guides.** Within a country GBIF counts
  where people recorded a plant, not where it grows: 308 records of sugar cane in the Philippines, 21 in
  the top province, and none of the top eight in Negros, which grows most of it. Across the world the
  same counts do say where a plant is: Khasi pine in the Philippines, China, Thailand, India, Vietnam and
  Myanmar.
- **A question about one country is looked up within it** ("where in the US can you see cholla?",
  "where can I find tree yucca in Mexico?"), when the place is a country and GBIF holds at least
  `MinRecordsInCountry` (500) records of the plant there. Below that the records say where somebody
  looked: 308 of sugar cane in the Philippines, none of the top eight provinces in Negros, which grows
  most of it. Such an excerpt follows the guides instead of leading them. Until 2026-11-10 a question
  with the country between "where" and the verb was not read as a plant question at all.
- A taxon with fewer than 25 records is not offered, and the lookup gives up after 6 s. GBIF failing
  never fails the question.

**The map** (`GbifSpeciesRangeMap`). Members asked for it twice: "Please show the GBIF distribution",
"I would have liked a distribution map". It is drawn from GBIF's own tiles, free and without a key:
its base map (`tile.gbif.org`, from OpenStreetMap data, credited in the picture's footer) and over it
the density of the species' records in hexagons, purple for few and yellow for many, a little
translucent so the names under them still read.
- The frame is the country counted (its mainland: the lower 48 states for the US, from
  `CountryBounds`, computed from Natural Earth's public-domain outlines), or the countries holding at
  least 5 % of the records each. A range that keeps to one part of the world is then cut down to
  where its records are, and drawn again at a finer zoom when that leaves less than 1,000 pixels; the
  world and a single country are shown whole.
- GBIF's density tiles take one filter, and it is the species. A map framed on one country therefore
  shows the neighbours' records within the frame too — cholla's map of the United States is purple
  well into Mexico — and its title says "records in and around United States". The counts in the
  excerpt's text are the country's alone.
- The picture is stored in the image relay and uploaded with the answer like any relayed picture, so
  it needs `AI:ImageRelay:PublicBaseUrl`. Maps are kept as long as the counts (`CacheHours`).
- It takes about a second (12 to 30 small tile requests) and has `Map:TimeoutSeconds` (8); whatever
  goes wrong, the range is offered in words.
- Look at one with the probe: `map "khasi pine"`, `map cholla --country US`.

Tool results are numbered excerpts that continue after the last number, in the prompt's own format.
Anything already offered is skipped. They are cited, listed and attached like any other excerpt, and
the list stops at [99], the highest number the citation resolver reads; the model is told when it is
full. When each tool is worth calling is said once, in that tool's description: a *partial* answer is
the reason (a question asking for every or exactly which places when the excerpts name some; a list cut
short; a user saying the guide says more; where a plant grows). The system prompt's tools paragraph
(about 100 words) says only what holds for all of them: several calls can go in one round, the user's
own words are searched first when the excerpts describe something related but different, and searching
comes before "the guides don't cover this".

**The loop** (`AiAnswerLoop`):
- Tools are offered only on a chain of tool-capable models, never to the fallback router, which picks
  a model at random. With no such chain, the question is answered in one call as before.
- A round's calls run together, and its searches share one embedding request.
- A search that offers nothing new says so. One that offers nothing from the country it was about (its
  `country` argument, or the countries its words name) is prefixed "None of these new excerpts is from
  X. If it is X you need, the guides seem to have nothing more on it than you have: answer with what
  you have." It is logged as `OffTarget`. A search naming no country is never judged by the question's
  country, because comparisons need the other countries' excerpts.
- After `MaxToolRounds` (2), after `AnswerDeadlineSeconds` (90) from the question's arrival, when the
  excerpt list is full, or when the allowance cannot pay for another round, the next call has to
  answer. It is sent with `tool_choice: "none"`, the tools still declared (some providers refuse a
  conversation holding tool calls once none are), and a last user message saying in words that no
  more tool calls are possible.
- **A tool call written as text is never posted.** With `tool_choice: "none"` alone, nemotron-3-ultra
  wrote the call out (`<tool_call><function=search_guides>…`) instead of answering in 6 of 49 replayed
  answers on 2026-10-29, and the bot posted it. Such a reply now goes to the rescue below, with the
  same model allowed; without tools it answered those questions correctly.
- **The rescue.** If a later call fails or does not answer, one last call is made without tools, with
  every excerpt gathered put into the single-shot prompt. It is not made when the provider can't be
  reached or is throttling our key.
- A provider that refuses tools ("No endpoints found that support tool use") gets the question again
  without them, from the same models, on the retry too. A retry after the deadline goes out without
  tools.

**Two rounds, because a third never helped.** In the 2026-10-29 replay with three rounds allowed, 11
answers took a third round and none cited what it found; six of them then wrote the call as text. The
second round was cited in 5 of the 12 answers that reached it on 2026-10-30.

#### Measuring whole answers

Replaying retrieval tells you what a question is offered; the probe's `answer` command tells you what
it is answered. Every rated answer of a feedback export runs through `AskAiHandler` and its real
collaborators: the embedder, the read-only index, the live model roster, the tool loop and GBIF. Only
these are replaced:
- the conversation store: one branch is rebuilt from the export, with the excerpts the previous answer
  cited where the export kept their chunk ids;
- the allowance, which is unlimited and fresh per answer;
- Discord: nothing is posted or stored, and relayed pictures are listed by their link;
- the HTTP resilience pipeline, which `PacedRetryHandler` mirrors.

`--judge` has a free model compare the old and new answer. Use one from another family than the model
that wrote the answers. On 2026-10-29 the judge was nemotron-3-ultra, which had written 47 of the 49
answers, and it never once said "same". Treat verdicts as indicative and read the answers. Discord
screenshot links expire within about a day, so screenshot questions cannot be replayed fairly.

Measured on 2026-10-30, all 49 rated answers of the 2026-10-23 export, judged by dots-3-note-preview:

| | Tools on (2 rounds) | Tools off |
|---|---|---|
| judged better than the rated answer (41 without screenshots) | 37 | 38 |
| tool call posted as text | 0 (6 on 2026-10-29) | – |
| answers with a picture | 44 | 42 |
| requests per answer | 3.16 | 2.00 |
| median time; answers that used tools | 14.4 s; 43.7 s | 10.8 s |

The judge could not tell them apart. Reading the answers can: tools won clearly on questions asking for
every place or comparing two countries (12 Gen 2 and 13 shitcam countries against 2 and 7; a full
Thailand–Philippines table against "no Philippine markers in these guides"). They lost in 2 of 17,
where a long prompt led the model to misread an excerpt it already had. `species_range` was not called
once, not even for the Khasi pine questions, although its description names that case; that is why
plants are now [looked up before the model answers](#tools-when-the-excerpts-are-not-enough).

### Naming the plant in a screenshot

Asked "what kind of cactus is this?" with a screenshot, the free chat models named the species of a
look-alike guide picture and gave that guide's country (2026-11-09 feedback). They cannot identify a
plant reliably and cannot say how sure they are. **Pl@ntNet** can: an image-based plant identification
service, free up to 500 identifications a day per account (reset at 00:00 UTC), commercial use allowed
within that, attribution appreciated rather than required (their pricing FAQ and terms, read
2026-11-12). The excerpt's source line reads "Pl@ntNet" and links to the service, which is the credit.

`PlantIdentificationLookAhead` runs beside retrieval, like the GBIF look-ahead, when three things hold:
`AI:Tools:PlantIdentification` is on **and has a key** (nothing is ever sent without one), the question
comes with a picture — its own attachment, or the branch's latest screenshot for a follow-up like "and
what tree is that?" — and the question asks what plant it shows (`PlantIdentificationQuestion`: a plant
word plus "what … is this", "identify", "is this a …"). A question about bollards is not sent because a
tree stands in the frame; each identification is one of the day's 500.

`PlantNetPlantIdentifier` downloads the screenshot (JPEG or PNG, up to `MaxImageBytes`), posts it as a
file to `POST /v2/identify/{Project}` with the key in the query, and reads the candidates with their
scores, common names and GBIF keys. What comes back is offered first among the excerpts
(`PlantIdentificationExcerpt`): the best candidate as *the* identification with its confidence when its
score clears `MinConfidence` (0.25 — a Street View frame is a poor photograph, so a clear match scores
0.3–0.6), as a guess below it, and the runners-up either way, so a member can tell a sure identification
from a toss-up. A best candidate over the bar is also looked up in GBIF by its scientific name and its
countries and map follow as a second excerpt, exactly as a "where" question gets them. The service's
"Species not found" becomes an excerpt saying so, so the model does not name a look-alike. The system
prompt tells the model to take the excerpt's identification and cite it.

Answers are cached by the screenshot's link without its signature (`CacheHours`, 24), so the usual
follow-up costs nothing. The adapter stops at its own `MaxRequestsPerDay` (450, counted in memory) before
Pl@ntNet's 500, and once the service answers 429 it asks nothing more that day. In both cases the member
is told beneath the answer (`AiAnswer.Notices`: "Plant identification (Pl@ntNet) has used today's
allowance; the plant was judged from the picture alone."), as they are when the service cannot be
reached; a guess must never pass for a lookup. A picture the service does not take (WebP, too large) is
only logged. Pl@ntNet keeps the picture in memory for the identification and logs the request, not the
image.

### A map of the regions an answer names

Answers often name first-level regions — "far north-eastern Vietnam: Lạng Sơn and Quảng Ninh" — and a
player who does not know where those are learns little from the names. After the answer is written,
`RegionLocator` reads it for region names and attaches a small map of their country with them coloured
in, numbered after the last source and listed with the sources as a picture.

- **Boundaries**: Natural Earth's 1:10m admin-1 states and provinces (public domain; the 1:50m file
  covers only a dozen large countries). `NaturalEarthRegionBoundaries` downloads the GeoJSON (about
  40 MB) once into `AI:Tools:RegionMap:DataDirectory` (`ai-data`), reads it on a background task
  started at host start-up (`RegionBoundariesWarmup`) and keeps each region's names (English, local,
  alternatives, the geonames one) and its outline, straightened by `SimplifyToleranceDegrees` (0.01°,
  invisible on a country map, a tenth of the points). A country's ISO code comes from `iso_a2`, or from
  the `iso_3166_2` prefix where Natural Earth writes `-1`. An answer never waits for the file: while it
  loads, there is no map.
- **Matching** (`RegionMention`, pure): the answer's and the question's countries come from the
  guides' gazetteer, as retrieval finds them; for each (at most three) the answer's text is searched
  for the country's region names — accents and case aside, whole words, the longest name winning
  ("North Sumatra" is not Sumatra). Names that are plain words (Ghana's *Central*, *Northern*, many a
  *Capital*) count only beside "region", "province" and the like. The country with the most regions
  named gets the map; one map an answer, at most `MaxRegions` (15) regions on it.
- **Drawing** (`RegionMapDrawer`): GBIF's base-map tiles framed on the country exactly as a species map
  is (`SpeciesMapPlan.ForCountry`, the same `CountryBounds`), the outlines filled red over them, each
  region labelled at Natural Earth's label point, the title and the credits (OpenStreetMap, OpenMapTiles,
  GBIF, Natural Earth) underneath; stored in the image relay like the species maps, so it needs
  `AI:ImageRelay:PublicBaseUrl`. Cached by country and regions for `CacheHours`.
- **Shades for how common something is**: the model can ask for a map itself through the `region_map`
  tool (`ToolEnabled`), giving each region a share from 0 to 1; the map shades them, darker where the
  share is higher, labels each with its percentage, and its title says so. Its description tells the
  model to call it when a clue is more common in some regions than in others, and not for regions named
  in passing, which the map after the answer covers without a call. When the model asked for one, the
  map after the answer is not drawn again.

Everything here costs no AI request. The map after an answer has `TimeoutSeconds` (8); whatever fails,
the answer goes out without it.

### The clue of the day

The lesser-known clues in the indexed guides are only seen when someone happens to ask the right
question. `AiClueOfTheDayJob` (`AI:ClueOfTheDaySchedule`, read in `AI:ClueOfTheDayTimeZone`) posts one to
`AI:ClueOfTheDay:TextChannelId` on a schedule; `/ai clue-now` posts one at once, in the configured channel
or — `here: true` — in the channel the command is given in, which works before the feature is turned on.

`PostClueOfTheDayCommand` draws clues the way a question for "a clue I don't know yet" does
(`ClueSampler.DrawAsync(count, excludeMainGuideClues)`: random picture chunks of the guides other than
PlonkIt, with words of their own, one per guide, those PlonkIt has a tip like left out while
`ExcludeMainGuideClues` is on), takes the first not posted within `NoRepeatDays` (180; the posts are kept
in `AiCluePosts`), and posts it shaped like an answer (`ClueOfTheDayPost`, pure): the `Heading` with the
guide's title, the guide's own words of the picture (cut at a sentence past 900 characters), the picture
as **[1]**, uploaded from the relay exactly as an answer's pictures are, and the guide cited beneath. The
posting goes through `IAiChannelPublisher` → `DiscordAiChannelPublisher`, which reuses the gateway's
answer-posting code without a message to reply to. The post is recorded whether or not the message went
out, so a failed post does not make the same clue the next day's. Costs no AI request.

Off by default: a channel has to be chosen first (`Enabled` with no `TextChannelId` fails start-up
validation). The schedule key must be present even while the feature is off, as every job's must.
### `/ai quiz`

A way to practise with the pictures the club has chosen to index. `/ai quiz [country] [lesser-known]`
draws one captioned guide picture at random (`DrawQuizQuestionQuery`: `IKnowledgeIndex.SampleAsync`
within the country when one is named — resolved through the guides' gazetteer, every spelling the index
files it under tried — keeping only pictures with words of their own, `ClueSampler.IsAClue`; with
`lesser-known`, PlonkIt's pictures and the clues PlonkIt has a tip like are left out) and posts it with
the picture uploaded from the relay. The answer — the country the guide files it under, the guide and
section, what the guide says of the picture, and the source link — sits behind **one spoiler**
(`AiQuizFormatter`, pure). Three buttons follow: *I got it* and *I missed* record a score the member
judges for themselves (`QuizStreaks`: streak, best streak and tally, in memory since the last restart —
a quiz is practice, not a league), answered ephemerally; *Another* draws again with the same filters.
Costs no AI request. `AI:Quiz:OnlyInAllowedChannels` keeps it to `AI:AllowedChannelIds` when that list
is set.

### `/ai review`: a played challenge, round by round

After a game a player sees where they were wrong, but not which clues would have told them.
`/ai review [challenge]` (`ReviewRoundsCommand`) takes a challenge link or token — or, with none, the
club's latest daily challenges — reads its highscores with the main club's account (as the country
challenges are evaluated), finds the member's own game by their linked GeoGuessr account (`/link`), and
reviews it:

- Each round and guess is placed in a country and region: GeoGuessr's own `streakLocationCode` where
  the results carry it, else the Natural Earth boundaries (`IRegionBoundaries.LocateAsync`, point in
  polygon over the admin-1 outlines the region maps use), so the review can say "Lạng Sơn, Vietnam" and
  "you guessed Guangxi, China, 410 km away". The round and guess fields of the highscores
  (`ChallengeResultRoundDto`, `ChallengeResultGuessDto`) are optional: the list was only ever read for its
  players before, and **their shape has not been verified against the live API** — a game that comes
  without them is reported as having no rounds rather than reviewed wrong.
- The guides are searched once per round, within the round's country under every spelling the index
  files it under ("How to recognise Lạng Sơn, Vietnam: distinctive clues of the region"), every round's
  wording in **one** embedding request; the rounds take turns at the excerpts up to `MaxExcerpts` (12).
- The model is asked once through the same answer loop a question goes through (`AiAnswerLoop`: tools,
  screening, retries), with the rounds written out (`RoundReviewPrompt`, pure) and told to name, per
  round, the clues that would have given the country and region and what sets the right country apart
  from the guessed one, ending with what to practise first. Citations, pictures and the source list come
  out as for an answer; the review is posted into the channel through `IAiChannelPublisher`.

Cost: two requests of the day's allowance (the embedding and the completion), claimed up front, plus the
tool rounds the model takes — the same as a question. The panorama itself is not looked at: the review
works from the round's place. A GeoNN-style guesser could be added behind a port later; it needs a GPU
host, which is outside "free to run" today.

### What goes into a vector

A chunk is not embedded as written. It is prefixed with the country it applies to and what it is
about, because a paragraph that only ever says "the bollards here" cannot otherwise be reached by a
question naming the country. Measured against the embedding model on real guide text:

```
bare chunk text                        0.2602
+ a non-topical title                  0.2619
+ a topic, no source structure         0.2913
+ the source's own topical heading     0.3781
+ both                                 0.3824
```

Two things follow. A topical header is the **largest single influence on retrieval** — larger than any
other tuning available here. And filler costs nearly everything the topic gains: a slide number, a
deck title repeated on every chunk, an author's byline. So the header carries the source's own heading
where it has one, a keyword-derived topic where it does not, and nothing else — non-topical segments
are stripped rather than tolerated, and a chunk with no recognisable subject gets no topic at all
rather than a meaningless one.

The topic is derived by keyword rather than by asking a model. Classifying every chunk with an LLM is
affordable against a self-hosted model and completely unaffordable against a metered one — thousands
of chunks would be thousands of requests from an allowance of a few dozen a day.

`EmbeddingTextBuilder.RecipeVersion` is folded into each source's content hash, so changing how the
header is composed re-indexes everything on its next scheduled run instead of silently leaving old
vectors in place.

**Link targets are dropped.** A quarter of PlonkIt's tips link example locations and species pages.
In those tips the URLs are a fifth of the characters, and random strings like
`maps.app.goo.gl/szUNXBWMYPjJ6jMA6` say nothing a question would. The extractor keeps only the link
text. Measured against the embedding model, that raised each linked tip's similarity to the question
it answers by 0.04–0.06, which is enough to lift "white car long antenna" from 39th to about 8th. It
also shortens the excerpts the model reads, and nothing downstream needed the URLs.

Only PlonkIt carries such links, so only PlonkIt pages re-index. Their chunk text changed, so each
page is re-embedded when its 14-day re-check comes due. `/ai ingest source-type: plonkit force: true`
does it sooner.

**Guide headings are part of the section path.** PlonkIt's divider and subsection titles were ignored
until measured. 1,892 of 5,276 tips (36%) sit under one, and a source's own topical heading is the
largest single influence on retrieval: Mongolia's tent tips were not found for "four band tent" while
their "Car metas" heading was missing. Guides nest the two heading kinds both ways round, so the kind a
step uses first is the outer level, and the "Step 1.2 -" numbering is dropped. Captioned pictures and
the prose between tips (text, centred text, notes, such as what the colours on Mongolia's car-meta maps
mean) are indexed too, and a guide's opening picture is captioned as its summary infographic. This
changed the chunk text of 137 of the 138 guides and added 147 chunks. Each guide is re-embedded when
its re-check comes due: about 240 text requests and about 570 picture requests in all, since relayed
pictures travel inline at about 0.5 MB each and only about 11 fit in one 8 MiB request.

**Google Docs are read from their page.** Until 2026-11-10 a Doc was read from its archive export,
whose pictures are the originals: "A Learnable Colombia" exports 218 MB for 171 pictures, and past
`MaxDocumentExportBytes` (32 MB) the text export was indexed instead. Measured on production, that was
**86 of the 185 indexed Docs without a single picture** — "A Unique USA" with its 1,044 among them —
and it is what members rated down ("I would have liked an image", of a clue whose document had lost
all of them). The Doc's mobile page (`/mobilebasic`) is the same document as half a megabyte to two of
HTML, read in about a second, with each picture a link that serves it at the size asked for
(`DocumentPicturePixels`, as a JPEG of about 270 kB). The archive and the text export remain, in that
order, for a page that cannot be read. `GoogleDocHtml` reads either HTML:
- **Sections nest as the headings do**, up to three deep: "A Unique USA › 7.20 Maryland (MD) › 7.20.1
  Signs › State Route Shield". Filed under the nearest heading alone, the same "State Route Shield"
  of every state said nothing of which state. A heading above the document's first is held at its
  level (authors mixing two heading styles), and a bold line above a paragraph is a heading too
  ("4. Oil Palms:"), taking the place of an open heading numbered like it ("Chapter 2" after "Chapter
  1"). The text export knows headings only by a line's shape: where two stood together, the ocotillo's
  range was filed without its name, and answered as the range of the cholla, and of the tree yucca.
- **A picture takes the words around it, and they are no paragraphs of their own.** Guides are written
  as cards: a title line, the picture, what it shows. The caption is the line above and, when that
  only introduces the picture, the paragraph below if it speaks of the same thing or points back at
  it; credits under a picture go with it; in a table row, the row's words. Captioned by its title
  alone, a picture reached the model as "Ohio gap **", and the model made up the rest.
- **A heading that can be linked to is** (`#heading=h.…`), so each cited section is a link of its own.
- A picture under a heading three levels deep with no other words is described by the path, and no
  longer counts as a heading-only picture.

Look at how a Doc would be indexed, without indexing it, with the probe's `extract <link>`. **Every
Google Doc is re-embedded once** after this release (its chunks and keys change): about 900 embedding
requests in all for roughly 15,000 to 20,000 pictures, at 25 sources a night unless
`/ai ingest source-type: gdoc force: true` is run. Downloading the pictures is paced at two requests a
second, so the largest Doc takes about nine minutes. The relay's disk grows by a few gigabytes.

**Google Doc section names.** A Doc that marks its sections with three or more heading elements is read
by those alone, and a heading element is refused only when it is a URL, a credit or has no words. A Doc
without them is read by the shape of its lines: a short line standing alone is a heading unless it is a
list item, nothing but a link, a URL, a credit ("Image credit to…", "Research by…", "@handle"), a
pointer ("click here", "…in more detail here"), more than eight words outside brackets, or an
all-lower-case remark of two or three words. Before 2026-10-30 any short line was a heading, so labels
read "Counties (33) ->" or "You can view the flags in more detail here", and the chunks under them were
filed under the wrong section. The same rule (`SectionLabel`) cleans labels when they are shown, so
chunks indexed earlier read better before they are re-indexed; losing a real heading is worse than
keeping a poor one.

Text chunk keys are positional, so a line that changes between heading and text shifts every later
chunk and changes the Doc's hash. **Each affected Google Doc is therefore re-embedded in full, once,**
at its next scheduled check after the release, within `ReingestAfterDays` (14). For about 185 Docs that
is roughly 75–100 requests a night for about a week at `MaxSourcesPerRun` 25, against production's
570-request indexing share. A label that changes without any chunk changing is not caught: the section
path is not part of the content hash, so that needs `/ai ingest force: true`.

### Conversations

A conversation is a tree of Discord reply edges, not a list. The context for a turn is the path from
that message up to the root, which is what keeps sibling branches independent.

Limits live under `AI:Conversation`. Idle time is measured from the branch's most recent message
rather than from the root, so a long discussion that is still active is never cut off mid-thread.
Trimming only ever drops the oldest turns — removing from the middle would leave a hole in the reply
chain. Images are capped separately at 2, because an image costs roughly 1800 tokens against a context
window that free models keep small.

**A follow-up carries the previous answer's excerpts.** Each answer turn stores the chunk ids it was
offered and cited, and its tool calls. A follow-up is offered the excerpts the answer it replies to
cited first (up to `AI:Retrieval:MaxCarriedExcerpts`, 6), read back from the index by id, then what was
retrieved for it; what the new search finds again is offered once. That is what a follow-up most often
points at: "show me an image of the distribution?" meant the map the previous answer had cited, and was
answered "I don't have the actual distribution image" when its own search offered one Mongolian excerpt
of eight, not that one. When the numbers the reader saw differ from the carried ones (two chunks of
one section were one number to the reader), each carried excerpt says which it was ("the user saw this
as 2"), so "can you show [2] bigger?" reaches the right picture. If a cited chunk did not come back, the
numbering cannot be trusted and nothing is said.

Stored questions are personal data. Retention is bounded and swept nightly.

### Feedback

Answers can be rated, and **a rated conversation is the only kind that is kept permanently.**

Two ways to rate, both landing in the same place:

- **React 👍 or 👎.** The bot prefills both on its own answer, so the affordance is visible without
  anyone having to know about it. Anyone in the guild can rate; each person's verdict is stored
  separately, and reacting the other way corrects your own rather than adding a second.
- **Right-click the answer → Apps → 👍 Good AI answer / 👎 Bad AI answer.** Same verdict, plus a
  comment box. Two menu entries rather than one because a message command may take only the message,
  and modals have no dropdowns — so a single entry would cost an extra click before the comment box.

Once an answer carries a verdict the bot adds ✅ to it. That marks *the answer*, not any one
reviewer — several people can rate the same one. It exists because a click that silently did nothing,
on an answer that has already aged out of `AI:Conversation:RetentionDays`, otherwise looks exactly
like one that worked.

**What gets stored, and when.** Ordinary turns live in `AiConversationTurns` under the retention
sweep above. The first time someone rates an answer, the branch that produced it — root down to that
answer — is *copied* into `AiAnswerFeedbacks` and `AiFeedbackTurns`, which the sweep never touches.
Unrated conversations still disappear on schedule. Removing your reaction deletes the copy again,
comment included: the conversation was only kept because of the verdict.

The copy is frozen at the moment it was rated. A branch keeps growing afterwards, and those later
turns are not part of what was judged; changing your mind later reuses the snapshot rather than
rebuilding it, since a posted message's ancestors never change.

**What the record carries.** The transcript, the model, the verdict and the comment — plus the guides
that were **offered** to the model for that answer, in rank order, and the ones it actually **cited**.
That last pair is the reason the archive is worth having: a bad answer where the right guide was never
retrieved and a bad answer where it was retrieved and ignored read identically in the transcript, and
they need completely different fixes. Each answer turn also carries the chunk ids it was offered and
cited, its cited guides, and the tool calls the model made (name, arguments, outcome), so a replay can
rebuild what a follow-up was carried and see what the model searched for. The excerpt text itself is
not stored.

**Reading it back.** `/ai feedback [days]` shows the counts, a per-model split and recent comments.
`/ai feedback-export [rating] [days]` returns the archive as a JSONL attachment — one conversation per
line, ready to hand to whatever is analysing it. Both are admin-only. Note that the export carries
Discord user ids off the server.

A long answer arrives as several Discord messages, and only the last is the stored turn; a reaction on
any earlier part resolves to the same answer, so rating the first chunk works as expected.

### The image relay

Some guide sites answer unattended clients with 403 — plonkit.net's image CDN is one. Since the AI
provider fetches image URLs **server-side**, those images are unusable no matter how relevant they
are. The relay copies them once during indexing and serves them from this bot instead.

Relayed images are sent to the embedding provider **inline**, as bytes, never by their relay URL. It
used to fetch them back through the bot's public address, and that failed intermittently in a way no
daytime test reproduced: behind Tailscale Funnel, the provider's fetches during the 05:00 UTC run came
back as Cloudflare `525`s, while the same images — one, a batch of 32, six batches back to back —
fetched fine at midday. Sending the bytes removes the question. Pictures are uploaded with answers, see below.

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
the fetcher is Discord's embed renderer — not a browser on your network — for the pictures that are
linked rather than uploaded.

If you expose the bot with Tailscale Funnel, this is the `https://<machine>.<tailnet>.ts.net` address
the funnel already serves; no extra routing is needed, since the funnel forwards everything to the
bot's port.

**On a dev machine**, tunnel the API the same way the Activity is tested (see the root `README.md`):

```bash
cloudflared tunnel --url http://localhost:5194
```

Put the `https://<random>.trycloudflare.com` hostname it prints into `PublicBaseUrl` and restart the
bot. A quick tunnel mints a **new** hostname every run, and a stored URL keeps the hostname it was
indexed under, but answers never show it as-is: a picture on disk is uploaded, and one whose file is
gone is linked under the `PublicBaseUrl` configured now. No re-index is needed after a hostname change.
(Embedding is unaffected too: the relay finds its copy by content hash, whatever host the stored URL
names.)

Turning the relay on does not by itself re-index anything: the change-detection hash is computed from
the source's own content, which has not changed, so already-indexed sources are judged unchanged and
keep whatever images they were indexed with. Use `/ai ingest force: true` once after enabling it.

#### Pictures are uploaded with answers

An answer uploads every relayed picture it cites and finds on disk, as a file on its final message.
The picture's embed points at the attachment (`attachment://picture-<n>.<ext>`), so the numbered,
linked layout is unchanged. Before this, Discord fetched each picture from
`{PublicBaseUrl}/api/v1/ai/images/…`. On 2026-10-22..24 Tailscale stopped publishing the funnel host's
public DNS (a control-plane bug after a sidecar update), and every relayed picture — 92% of the indexed
ones: PlonkIt, Google Docs and Slides — silently failed to show while the bytes sat on the bot's disk.
Answers were rated 👎 "I would have liked an image" although the model had cited the picture. Pictures
from hosts that serve their own (imgur, rmrg.me) are still linked. A relayed picture is linked instead
of uploaded when:
- its file is not on disk (logged at Warning);
- it is over `MaxUploadBytesPerPicture` or past `MaxUploadBytesPerAnswer`;
- it would be the 11th (Discord's per-message limit for embeds and attachments);
- the upload fails, whether Discord refuses it or the connection does (logged at Warning). The bot then
  posts the same text with linked pictures, so the answer still arrives.

Linked relayed pictures are always moved onto the `PublicBaseUrl` configured now, never shown under the
host they were indexed under. The bot needs the **Attach Files** permission in its AI channels; without
it every upload is refused and pictures fall back to links.

`/ai status` → **Picture links** runs `GET {PublicBaseUrl}/health` with a 5 s timeout. A failure is
conclusive and shows the resolver's own message (e.g. `Name or service not known (host:443)`). A
success proves less: the request leaves from the bot's own host, whose resolver may answer for the name
privately (MagicDNS does for a tailnet name) even while the public record is gone.

### Redirects and the referer

The relay follows redirects itself rather than letting `HttpClient` do it, because the referer has to
be rewritten at each hop. Hosts of this kind send a request for `www.example.net` on to a regional
mirror such as `de.example.net`, and then check that the referer names the mirror: a referer still
pointing at the site the redirect came from reads as hotlinking and is refused. Measured on
plonkit.net, that one header is the whole difference between `200` and `403` — from any IP, browser
user agent or not.

It covers two different problems with one mechanism. Images **linked** from a host that blocks us are
downloaded and re-served. Images **embedded** in a Google Doc or slide deck have no URL at all, so
their bytes are pulled straight out of the export and stored the same way — which is why documents
only contribute pictures when the relay is on.

Stored images live in `AI:ImageRelay:Directory`, backed by the `ai-images` volume in `compose.yaml`
so they survive a redeploy. Losing them would leave every stored image URL pointing at a 404 until
the library was re-indexed, and answers would fall back to linking them, with a Warning per picture.

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
/ai status           # models, indexed chunks, source counts, whether the picture host answers
/ai search "bollards yellow"   # what retrieval returns, without asking a model
/ai sources          # sources that failed, were skipped or are unavailable, and why
/ai source-retry source-type:rmrg   # queue them again once fixed upstream
```

After that a nightly job drains the queue on its own.

### What can be indexed

| Source | Notes |
|---|---|
| plonkit.net country guides | Full text **and** images. Discovered from the site's own sitemap (~157 pages). Each tip is one chunk: its prose and its one picture, under the guide's divider and subsection titles. Captioned pictures and the prose between tips are indexed too; videos and navigation buttons are not. |
| rmrg.me country guides | Full text **and** images. Discovered from the site's own sitemap (13 guides). Fewer countries than plonkit and far more detail in each — ~30–180 clues per country, every one a picture paired with the prose describing it. Its images need no relay entry: they are served to anyone who asks. Both the original markup and the redesign live since October 2026 are read; the redesign's picture URLs carry a site-wide release token, so they are stored by the stable `rmrg.me/guides/{path}?v=<guide's update date>` address instead, or every site deploy would re-embed every picture. |
| imgur albums | Infographics — often the most useful artefact for a meta. Images indexed. |
| Google Docs | Text under its nested headings, plus its pictures when the relay is configured, each captioned by the words around it. Read from the document's mobile page, so no document is too large; the archive export and then the text export are read only when the page cannot be. |
| Google Slides | Text, speaker notes, and embedded images when the relay is configured. A deck Google refuses to export (413) or larger than `MaxDeckExportBytes`, and every deck when the relay is off, is indexed from its text export instead: no pictures, speaker notes or slide links. |
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

A run also stops after `MaxSourcesPerRun` sources (25 by default), whatever allowance is left — and
after the top-up, that is the limit you hit first.

| Allowance | `MaxSourcesPerRun` | Sources per night | ~590 sources |
|---|---|---|---|
| 45/day (free tier) | 25 | ~13 | about six weeks |
| 950/day (after $10) | 25 | 25 | about three weeks |
| 950/day (after $10) | 300 | ~280 | about two nights |

A run that stops early leaves untouched sources at the front of the queue, so successive runs resume
rather than repeating. A run takes sources in this order: never indexed, then sources owed pictures,
then re-indexes that have come due, and last those whose previous attempt failed. Failing sources go
fewest failures first, and at most `MaxFailingRetriesPerRun` of them per run, so a backlog of dead
links cannot crowd out work that will succeed. A source a sync stops listing is tombstoned rather than
deleted; after `RemovedSourceGraceDays` its chunks are removed from the index.

### Descriptions for pictures without words

Many guide pictures carry no caption and no prose around them: a Google document's picture under a
bare heading, a slide of nothing but a photograph. Such a chunk's text is its guide's title or its
section path, which a written question never matches, and when a picture like that is shown the
answer cannot say what it shows — the model knows a picture only by its text.

A free vision model describes them. Each picture whose only words are a short heading
(`PictureWords.IsOnlyAHeading`, the same rule retrieval demotes such pictures by) is flagged
`captionWanted` when it is written. `CaptionPicturesCommand` — the `PictureCaptionJob` at
`AI:CaptionSchedule` (04:30 UTC, before indexing), or `/ai describe-pictures` — reads flagged pictures
that have no description of the current `AI:Captions:Version`, sends each inline to the best free
model that reads pictures (`ChatModelRequirements.NeedsImageInput`, the same ranking a screenshot
question uses), and stores what comes back as `generatedCaption` on the point, with the version. The
prompt (`PictureCaptionPrompt`) asks for what a player recognises a place by — poles, bollards,
signs, script, vegetation, what a map highlights — in at most 60 words, and forbids guessing the
country, since a guessed one would be searched and shown as if the guide had said it. A reply that is
a refusal, a safety verdict or token soup is not stored, and the picture waits for another run; after
`MaxConsecutiveFailures` (3) such replies in a row the run stops, because the model is not describing
anything today.

Descriptions are embedded: the group's text vectors are rewritten with the description appended to
the chunk's text (`EmbeddingTextBuilder`, the same recipe a fresh ingest uses), so a question about
"a white bollard with a red band" now finds a picture that had only "Tunisia" to its name. The one
embedding request a group of 32 costs is claimed *before* any picture of the group is asked about, so
a description paid for always gets its vector. A re-ingest of the guide keeps the descriptions of
pictures whose URL has not changed (`ReadGeneratedCaptionsAsync`), because each cost a request.

What the model and the member see:

- The answering model is shown the description inside the excerpt, after the guide's own text and
  marked `(AI description of the picture, not the guide's words: …)`, so it can say what a picture
  shows without quoting the description as the guide's.
- A member sees it under a picture only when the guide itself says nothing of it, prefixed
  **AI description:** — and only while `AI:Captions:ShowToMembers` is on. Off, descriptions still make
  pictures findable and inform the model, and nothing generated is ever shown.
- A described picture is no longer "heading-only" for retrieval's demotion, the clue sampler or the
  read-guide tool (`KnowledgeHit.IsUndescribedPicture`); `IsHeadingOnlyPicture` still says what the
  guide's own words amount to.

Cost and pace: one chat request a picture plus one embedding request a batch, claimed against
`AI:Captions:MaxDailyBudgetPercent` (20%) of the shared daily counter. The job runs before indexing so
its share is claimed first; indexing then claims up to its own 60% ceiling and answers have what is
left. At `MaxPicturesPerRun` 60 a night, a few thousand flagged pictures take a couple of months, which
is the point of the pace: `/ai describe-pictures count:…` goes faster when the allowance allows, and
`/ai status` shows what the day has spent. Raising `Version` re-describes everything over the following
runs — after a prompt change, or once a better free vision model is around. `Enabled: false` stops the
job and the command without touching what is stored.

### When a source cannot be read

Every extractor classifies a failed fetch the same way (`SourceFetch`), and the run acts on each kind
differently. Before this, 12 Google sources that were deleted (410) or too large (413) failed again
every night.

| The host answers | Recorded as | What happens next |
|---|---|---|
| 404 / 410: deleted. Google answers 410 on every endpoint of a deleted file; imgur answers 200 with an empty list | Unavailable | Checked again after `UnavailableRecheckDays`. The second check in a row that still finds it deleted removes its chunks, so answers stop citing a dead link |
| 401 / 403 from Google: not shared publicly | Unavailable | Checked again after `UnavailableRecheckDays`, and back by itself once shared again. Its chunks are kept |
| 401 / 403 from a guide site (PlonkIt, rmrg, imgur) | Left untouched | The site refuses us as a whole, so the rest of that host is left for the next run |
| 413, or an export over the size cap | Slides: text export indexed. Docs: only reached when the page could not be read; then the text export. Others: Skipped | Text only, no pictures |
| 429 after the retries, or the circuit breaker is open | Left untouched | The rest of that host is left for the next run; the source keeps its place in the queue, and its slot goes to other hosts' sources |
| 5xx / 408 / timeout | Failed | Retried with backoff (2, 4, 8 … 64 hours). Docs and Slides read the text export meanwhile; if pictures are already indexed they are kept and the attempt counts as failed |
| Read, but not a guide or empty | Skipped | Not retried until `/ai source-retry` |

Every Failed, Skipped or Unavailable source is logged at Warning as `type:key (code): reason`. The run's
summary has a second line counting each kind: deleted, no longer shared, too large, left because the
site refused requests, and removed from the index.

`/ai sources [status] [source-type]` lists the sources needing a look, each with its reason, how many
times in a row it failed, and when it was last tried and will next be tried. `/ai source-retry` queues
failed, skipped or unavailable sources again, by key (also `type:key` as the listing shows it, or the
link) or by source type.

**After upgrading,** sources skipped under the old rules stay skipped until queued: documents recorded
as "not publicly readable", imgur albums recorded as "has no readable images", and guides recorded as
"not a guide page" after a site redesign. Every rmrg.me guide re-read after its October 2026 redesign
was skipped this way. Review them with `/ai sources status:skipped` and run
`/ai source-retry source-type:<type>` once per type.

### Rate limits and images that fail

The $10 top-up raises the daily allowance but **not** the per-minute one: free models stay at 20
requests a minute. Requests are paced evenly under `PerMinuteRequestBudget` (18) — a small burst, then
one token every few seconds — because a limiter that refills its whole minute at once lets a burst
straddle the refill and send twice the budget in seconds. Retries queue for a token like any other
request.

When the provider still answers 429, the request waits for its window to turn over (the stated reset,
or the next wall-clock minute) and tries once more. A chat completion's 429 from the model's upstream
shared pool is the exception: waiting for our window cannot fix it, so it is handed straight back and
the question is retried with other models. If *that* is refused too, something else is
spending the same key — often a dev instance running with the production key — and the run stops,
leaving the source it was on untouched and first in line for the next run. It is not recorded as a
failure, so it does not back off.

Images in a format the provider cannot read never reach it. It refuses anything it cannot decode as a
picture — vector images above all, and rmrg.me draws a good share of its illustrations as `.svgz` —
and a refusal fails the whole request the image travelled in, which is then narrowed one request at a
time to find the culprit. Those chunks are indexed on their text alone, and the image URL is dropped
rather than stored: Discord will not render one either. Only formats *known* to be unusable are turned
away (`.svg`, `.svgz`, `.avif`, `.bmp`, `.ico`, `.tif`, `.heic`, `.pdf`); an extensionless URL is
still tried, because plenty of hosts serve ordinary JPEGs from one.

The rest are embedded in groups, and a group's failure is judged by its cause:

| What happened | What ingestion does |
|---|---|
| The provider rejected the group and named the image at fault | Drops that image and re-sends the rest in one more request |
| The provider rejected the group without naming anything | Halves it until the image at fault is found; indexes the rest, and that image without its picture |
| More than three images rejected in one source | Stops narrowing — that reads as the provider refusing images in general — and comes back for them next run |
| The provider failed, timed out, or the allowance ran out | Keeps what succeeded and comes back for the rest next run |
| Still rate-limited after waiting | Keeps what succeeded, comes back for the rest, and ends the run |

A source that is "coming back" is due again immediately instead of waiting out `ReingestAfterDays`.

Sources indexed before this behaviour existed may have lost their images for good: a failed batch
used to leave the source recorded as fully indexed, and unchanged content is never embedded again.
Recover them once with:

```
/ai backfill-images
```

It asks the index which image chunks have no image vector, queues their sources, and lets the normal
runs re-embed them.

---

## Known limitations

- **Images anywhere need the relay configured.** Without `AI:ImageRelay:PublicBaseUrl`, images from
  blocked hosts keep their original links (which the provider cannot fetch) and embedded document
  images are skipped entirely, so those sources are indexed text-only. An image that still cannot be
  stored costs a picture, never the source it belongs to.
- **Indexing documents costs far more bandwidth once the relay is on.** A Google Doc's text export is
  around 2 KB; the zip export carrying its images is around 1.5 MB, and a slide deck about 7 MB. The
  heavier export is only fetched when images can actually be served, and the nightly job is paced for
  it — but a full re-index moves hundreds of megabytes rather than a few. A slide deck's export is a
  median of 19 MB, 339 MB for the largest in the library.
- **Tools are used less than they could be.** Whether the model calls a tool is its own choice. In the
  2026-10-30 replay `species_range` was never called (plants are now looked up before answering
  instead, for questions about where in the world one is found), `read_guide` once, and a question about
  "snake traps" was still answered from the possum-guard tip without a search for the user's words. Tool rounds
  also make an answer slower (a median 44 s for answers that used them) and hold one of
  `MaxConcurrentRequests` slots for longer.
- **A long tool context can be misread.** With 20–30 excerpts in the prompt, the model twice
  contradicted an excerpt it already had ("Roraima Antenna common" read as "no antenna"), where the
  same model without tools read it correctly.
- **Species counts are records, not abundance**: observation effort, herbaria, gardens and plantations
  all count, and the map shows the same records. Crops are hardly recorded at all, which is why a
  plant needs `MinRecordsInCountry` records before a country question is answered from them.
- **A picture's caption is read from the layout, not seen.** `GoogleDocHtml` decides by the lines
  around a picture which of them describe it. A picture nobody describes in words (a map under "Ex:
  Silver car" and "Credits to cron") is still found only by what stands near it and by its pixels.
- **"Also on PlonkIt" is a similarity, not a reading.** Two different clues of one state that share
  their words can be marked (North Carolina's white lines beside its white signs), and a clue PlonkIt
  tells in other words can be missed.
- **A screenshot's plant is named by a free vision model**, which can be wrong; the GBIF lookup then
  shows where that guess grows.
- **The planner still guesses countries** for questions that name none (Khasi pines → India), and
  vocabulary misses remain: Mongolia's "four band / open tent" against the guide's "4 straps / open
  zipper". Literal phrases would need a full-text index on `text`, which is not created on an existing
  collection.
- **Screenshot questions cannot be replayed later.** Discord attachment links expire within about a
  day, so the feedback archive keeps the question but not the picture.
- **A strange answer may be the model, not the bot.** Free models vary wildly. Screening catches a
  safety classifier's verdict, not a weak model's weak answer. The footer names the model and 👎
  records it — check `/ai feedback` for a model with a lopsided split before assuming the pipeline is
  at fault.
- **The model cannot see the guide pictures it attaches.** It is given only their text, so it says what
  a picture is for rather than what is in it. Sending the pictures themselves would cost roughly 1800
  tokens each, against context windows free models keep small.
- **Answer quality varies with whatever is free today.** Auto-selection ranks by stated size and
  provider health, which are proxies for quality and do not measure it. Use `PreferredModelPrefixes` to steer it, and the model named in each answer's footer
  to work out what to steer towards.
- **Guild channels only.** Direct messages are ignored: they bypass the channel allowlist and are an
  easy way to drain the allowance.
- **An answer that has aged out cannot be rated.** Feedback archives the conversation, and past
  `AI:Conversation:RetentionDays` there is nothing left to archive. The reaction is simply ignored.
- **Clearing reactions does not delete feedback.** Only removing your own 👍/👎 does. A moderator
  clearing all reactions on a message leaves the stored verdicts in place.
- **Feedback left through the right-click menu has no reaction to take back.** Removing it means
  deleting the row.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Replies to the bot do nothing | Message Content intent not enabled in the Developer Portal |
| Bot is entirely offline after enabling AI | Same, but the intent *was* requested — gateway close code 4014 |
| "I'm out of free AI requests for today" | Daily allowance spent; resets 00:00 UTC |
| `/ai status` shows 0 indexed chunks | Nothing indexed yet — run `/ai sync-sources` then `/ai ingest` |
| Answers ignore the guides | Check `/ai search` first: it costs one request and shows what retrieval returns. For a whole feedback export, `Tools/GeoClubBot.RetrievalProbe replay` does the same for every rated answer, and `answer` asks every rated question again through the real answering code |
| Guide images show as broken in Discord | Uploaded pictures cannot break; a broken one was linked. Check the log for "is not on disk" (the relay volume lost files) or "Uploading … picture(s) … failed" (grant Attach Files, or lower `MaxUploadBytesPerAnswer`), and the **Picture links** line of `/ai status` for whether `PublicBaseUrl` answers |
| Answers cite a picture, but none is attached and `/ai status` says the picture host is unreachable | Only linked pictures depend on the public address. If it is a Tailscale Funnel name that stopped resolving, restart the Tailscale sidecar, and pin its image rather than running `:latest` |
| Catalog source is `None` | The model roster could not be read; the fallback router is in use, and its answers are screened |
| An answer reads `User Safety: safe` | A safety classifier answered and screening did not recognise the format — add the model named in the footer to `AI:OpenRouter:BlockedModelIds` |
| Log: "… is excluded until restart" | A model answered with a classifier's verdict and was caught by its answer; `/ai status` lists it. Add it to `AI:OpenRouter:BlockedModelIds` to keep it out after a restart |
| "The AI model I reached couldn't give a real answer" | Both attempts came back as something that is not an answer; the log names the models |
| "The AI model I tried took too long to answer", or the log says "… did not finish its completion" | A model began a reply and stalled, or was simply slow; it was cut off after `AI:ResponseTimeoutSeconds`, demoted for a while, and other models were asked. The member sees the message only when the models asked next failed too. If replies you would have waited for are cut off, raise the setting, and keep `AI:OverallTimeoutSeconds` well above it |
| Log: "… returned garbled text — words that change alphabet half-way" | A provider served token soup as a valid completion; the reply was discarded and other models asked. Nothing to do unless one model does it often: then add it to `AI:OpenRouter:BlockedModelIds` |
| Text or a source label shows `&##128128;` where an emoji belongs | Indexed before emoji were decoded correctly (HtmlAgilityPack's `DeEntitize` cannot decode them; `HtmlText.Decode` replaced it). The guide's text changed, so it re-embeds at its next re-check; `/ai ingest force: true` does it sooner |
| `/ai ingest` stopped early, "kept rate-limiting" | Something else is using the same API key — check for a dev instance with the production key |
| Indexing is slow even after the $10 top-up | `MaxSourcesPerRun` caps each run at 25 sources; raise it |
| Image search misses pictures that should be indexed | Run `/ai backfill-images` once; images lost before failed batches were retried are re-queued |
| A guide has no images at all, but its text is indexed | Its pictures are vectors (`.svgz`), which no part of this pipeline can use — expected, not a fault |
| A Google Doc is indexed but has no images | It was indexed before Docs were read from their page: it is re-read at its next check, or at once with `/ai ingest source-type: gdoc force: true`. If it still has none, its page could not be read and its archive was too large; the log says so |
| Log: "Stopped downloading the pictures of Google document …" | Five pictures in a row failed, or Google answered 429. What was read is indexed and the rest left for a later run; a copy already indexed with its pictures is kept |
| Log: "The page of Google document … holds no document" | The page answered with something else (an uploaded Office file, a sign-in form); the exports were read instead |
| A plant question got no map | The plant has fewer than `MinRecordsInCountry` records in the country asked about, the relay has no `PublicBaseUrl`, or the log says "The GBIF map of … took over 8 s" / "could not be drawn" |
| Log: "The question asks for something the user does not know yet …" | Informational: PlonkIt's tips were left out of that question's search |
| "The archive export of Google document … failed" or "The PowerPoint export of Google Slides deck … failed" | Google timed out or failed building the export. The text is indexed instead; a source that already has pictures keeps them, and the attempt is retried with backoff |
| `/ai sources` shows a guide Unavailable, "no longer exists (HTTP 410)" | Its owner deleted it. Nothing to do: it is rechecked every `UnavailableRecheckDays` and its chunks are removed on the second check |
| Unavailable, "not publicly readable (HTTP 403)" | Its owner stopped sharing it. It comes back by itself within `UnavailableRecheckDays` of being shared again, or at once with `/ai source-retry key:<type:key>` |
| Every guide of one site is Skipped, "carries no guide content" | The site changed its markup. Once the extractor is fixed, run `/ai source-retry source-type:<type>` |
| Run summary: "left for a later run because their site refused requests" | The host rate-limited, refused us with 401/403, or the circuit breaker opened. The sources keep their place for the next run |
| A slide deck is indexed but has no pictures | Google refused to export it (413), or it is larger than `MaxDeckExportBytes`. Its text is indexed |
| Log: "AI model … or refused to serve this bot, and is excluded until restart", after a 403 | A free model reserved for agentic harnesses refused the bot. Add it to `AI:OpenRouter:BlockedModelIds` |
| Log: "Read the provider health of N AI model(s) …; below the uptime bar: …" | Informational. The models named are left out of chains until a later refresh finds them healthy |
| Log: "Could not read the provider health of AI model …" | The endpoints read failed; the model counts as healthy. Harmless unless it happens for every model |
| Log: "Query planner gave no answer within 20 s …" | The model heading the chain is slow. Prefer a faster model (`PreferredModelPrefixes`) or turn the planner off |
| Log: "AI tool call (round N) search_guides …: Unavailable" | The round's embedding failed, or the allowance couldn't pay for it; the model answered with what it had |
| Log: "After tool round N the AI model has to answer: …" | Informational: rounds used up, deadline passed, excerpt list full, or allowance short |
| Log: "AI model … wrote a tool call as text instead of answering" | Expected now and then from nemotron-3-ultra; the answer is asked for once more without tools. Nothing to do unless it happens on most tool answers |
| Log: "Asking for the AI answer once more after …, without tools …" | A later call failed or did not answer; the single-shot rescue ran |
| Log: "The day's AI request allowance ran out during tool round N" | A concurrent question took the last requests; the user got the budget message |
| Log: "The AI provider refused the tools for …" | The provider can't do tool calls for that chain; the question was asked again without them |
| Log: "GBIF could not be read for '…'" | GBIF was down or slow; the model was told so |
| Log: "The question asks where … is found: offering GBIF's …" | Informational: a plant was looked up before answering |
| Log: "Looking up '…' in GBIF took over 6 s" or "GBIF could not be read for '…' before answering" | GBIF was slow or down; the question was answered without it |

`/ai search` is the tool to reach for. It costs a single embedding request instead of the two a full
question costs, and shows exactly what the model would have been given — which is usually the
difference between "the model is bad" and "nothing relevant was retrieved".
