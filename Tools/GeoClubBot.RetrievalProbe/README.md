# GeoClubBot.RetrievalProbe

A **read-only** console tool that replays questions through the bot's own retrieval — and, with
`answer`, its whole answering code — against a knowledge index, which is normally production's.

## Why it exists

`/ai feedback-export` hands you rated answers. Each record lists the guides the answer was *offered*
and the ones it *cited*. A bad answer has two causes that read identically in its transcript:

- **the right guide was never offered** — a retrieval or ingestion problem;
- **it was offered and ignored** — a prompt or model problem.

This probe tells them apart. It also measures a proposed change to retrieval against the real index
before the change ships.

It replays through the bot's **own code**: `QdrantKnowledgeIndex`, `KnowledgeHitFusion`,
`OpenRouterEmbedder` and `EmbeddingTextBuilder`. It is not a re-implementation, so what it measures is
what users get, including changes made after this tool was written.

It was written after the first beta-test export (September 2026). The fusion weights and exact
search the bot uses today came out of that measurement; see
[How the lists are weighted](../../Documentation/AiGuide.md#how-the-lists-are-weighted-and-why-measured-on-production).

## Read-only by construction

The probe is pointed at production, so it must never change anything there. Unlike
[`GeoClubBot.ApiProbe`](../GeoClubBot.ApiProbe/README.md) it references the bot, because it has to in
order to run the bot's code. That puts write-capable code within reach, so four independent layers
stand in for "no references":

1. **`ReadOnlyQdrantInterceptor`.** Every gRPC call to Qdrant passes an allow-list of reads: listing and
   reading collections, queries, query batches, scrolls, point reads and counts, and the health check.
   Anything else throws before it reaches the network. It is an allow-list rather than a block-list,
   so a new kind of write in a future Qdrant release is refused by default.
   `RetrievalProbeIntegrationTests` proves this against a real Qdrant: an upsert, a sweep's delete and
   a collection delete are all refused, and the index is unchanged afterwards.
2. **`EmbeddingsOnlyHandler`.** The only request allowed to leave for OpenRouter is
   `POST /api/v1/embeddings`. A chat completion, which is where the bot's real cost lies, cannot be sent
   by mistake. Every request that does leave is counted, and the count is printed at the end of each
   run. The one exception has to be asked for by name: with `--allow-chat`,
   `POST /api/v1/chat/completions` may leave too, and the two reads the bot picks a model with — the
   `GET /api/v1/models` roster and a model's provider health, `GET /api/v1/models/{author}/{slug}/endpoints`
   — through handlers built for the planner and for `answer` alone. Nothing else does: no route that
   changes the account, no other method on these. The embedder's handler of `replay` and `compare` never
   lets a chat request through, flag or not. `answer`'s calls to GBIF, for the species-range tool, pass a
   handler that lets only `GET` through.
3. **No verb in the CLI.** Every command reads. There is no option that writes.
4. **A cache.** Question vectors are kept in `embedding-cache.json` and planner replies in
   `planner-cache.json`, next to the built binary, so rerunning a report costs nothing. Vectors are
   keyed by embedding model; planner replies by exactly what was sent (and by the chain, when
   `--planner-models` names one). `answer` keeps each answer, and each verdict, in `answer-cache/`
   there too (see [`answer`](#answer)).

## Setting up

### The OpenRouter key

Questions have to be embedded before they can be searched. That costs **one request per 32 new
questions**, out of the key's daily allowance, which is shared with the bot if you use its key.
`grep` embeds nothing and needs no key.

The key is read from the first of these that has one:

1. the `OPENROUTER_API_KEY` environment variable;
2. `appsettings.Local.json` beside this project:
   `{ "OpenRouter": { "ApiKey": "sk-or-v1-…" } }`. The repository's `appsettings.*.json` ignore rule
   covers this file, so it cannot be committed by accident;
3. the bot's development settings, `GeoClubBot.API/appsettings.Development.json`, under
   `AI:OpenRouter:ApiKey`. It is already there if you run the bot locally.

The key is never printed. The run log says which source it came from.

### Reaching the index

The probe speaks Qdrant's **gRPC** API, on port 6334 rather than HTTP's 6333. It connects to
`http://localhost:16334` unless told otherwise, via `--qdrant`, the `QDRANT_GRPC_URL` environment
variable, or `{ "Qdrant": { "Url": "…" } }` in `appsettings.Local.json`.

**Production.** Qdrant usually publishes no host port and is reachable only on its compose network.
An SSH local forward to the container reaches it without changing anything on the server:

```bash
# On the server: the Qdrant container's address on its compose network.
docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' <project>-qdrant-1

# On your machine: forward localhost:16334 to it, and leave this running while you probe.
ssh -N -L 16334:<container address>:6334 <server>
```

The container address changes when the stack is recreated, so look it up each time.

**Locally.** Run `docker compose up qdrant`, then pass `--qdrant http://localhost:6334`.

The collection defaults to the bot's own, `geo-knowledge-<embedding model fingerprint>-<dimensions>`,
derived the way the bot derives it. `--collection` overrides it. If the collection does not exist, the
probe lists the ones that do.

## Usage

```bash
dotnet run --project Tools/GeoClubBot.RetrievalProbe -- <command> [options]
```

| Command | What it answers |
|---|---|
| `replay <export.jsonl>` | For every rated answer: what retrieval offers for its question now, what it was searched by, where each excerpt came from, and what changed since the rating |
| `compare <export.jsonl>` | The same questions under other retrieval settings — the bot before conversation-aware retrieval, each step of it, other excerpt counts — counted side by side |
| `grep <regex>` | Every indexed chunk whose text matches. Was the answer there to find at all? |
| `similarity` | How close a guide's chunks sit to a question, as stored and as re-worded (`--question`, `--source`, `--variant`) |
| `answer <export.jsonl>` | Every rated answer asked again through the bot's own answering code — retrieval, model chain, tool calls — shown as Discord would post it, beside the rated one; with `--judge`, compared by a model. Needs `--allow-chat` |

| Option | Meaning |
|---|---|
| `--qdrant <url>` | Qdrant's gRPC address |
| `--collection <name>` | Collection to read (default: the bot's own) |
| `--limit <n>` | Excerpts per question (default: `AI:Retrieval:ExcerptLimit`'s default, as the bot offers); `answer`: the most rated answers to ask |
| `--rating good\|bad` | `replay`/`compare`/`answer`: only answers rated this way |
| `--targets <file>` | `replay`/`compare`: count on-target excerpts, place known answers, stand in for expired screenshots (see below) |
| `--allow-chat` | `replay`/`compare`: let the query planner's chat requests leave the probe — one per question, cached. `replay` then searches with the planner on (it ships off); without it, `compare`'s variants that plan search as the bot does when the planner fails. `answer`: required, since it asks the bot's models |
| `--planner-models <ids>` | `replay`/`compare`: comma-separated chain the planner asks instead of the bot's own, to measure another model; cached apart |
| `--set <Key=Value>` | `answer`: overrides one of the bot's AI settings, e.g. `--set AI:Tools:Enabled=false`; repeatable |
| `--only <list>` | `answer`: record numbers and ranges, feedback-id prefixes, `after:`/`before:` dates, comma-separated (`1-25,31`, `after:2026-09-27`) |
| `--parallel <n>` | `answer`: rated answers asked at once (default 2, at most 8) |
| `--judge` | `answer`: a model compares each new answer with the rated one — one request each, cached |
| `--judge-model <ids>` | `answer`: comma-separated chain the judge asks (implies `--judge`; default: the best free text model of today's chain) |
| `--variants <file>` | `compare`: retrieval settings to compare instead of the built-in steps (see below) |
| `--question <text>` | `similarity`: the question |
| `--source <url>` | `similarity`: a chunk's source URL, as listed under an answer or in a replay |
| `--variant <text>` | `similarity`: re-worded chunk text to try; repeatable |
| `--max <n>` | `grep`: matches to list (default 20) |
| `--out <file>` | Also write the report, which is Markdown, to a file |

Keep exports and reports in `ai_feedback/`, which is git-ignored. Exports carry Discord user ids and
conversations.

## Reading the output

### `replay`

This is an abridged example from the first export's Khasi pines question:

```
## 👎 , where can I find Khasi Pines?
> While the answer is correct, Khasi pines are also found outside of India. …

| # | text | pixels | excerpt                                                             | guide |
|---|------|--------|---------------------------------------------------------------------|-------|
| 1 | 1    | –      | Khasi pine- super common in Meghalaya, specifically west Meghalaya. | … ✓ |
| 5 | 7    | –      | Plantations of khasi pines are also common around the center of…   | … ★ |
| 6 | –    | 1      | 🖼 The final type of coverage is a number of tiny forest paths.     | … |
```

- **text / pixels**: the excerpt's rank when the lead wording is searched against chunk text, and
  against picture pixels. "–" means it is not in that search's top 40, so row 6 was found by its pixels
  alone.
- **✓**: the rated answer cited this excerpt. **★**: it was not offered when the answer was rated, which
  is what a retrieval change should show on the question it was meant to fix. **◎**: on target, when
  `--targets` is given.
- Above each table, **Searched by** lists every wording the question was searched by and its weight —
  for a follow-up its branch, with `--allow-chat` the planner's rewrite and phrasings — the countries
  boosted and the key phrases.
- Above each table, **Offered now** counts how many of the guides the answer was given are still
  offered, and how many are new. Below it, **No longer offered** lists the ones that dropped out.

To triage a bad answer, check the offered list first. If the guide that answers the question is
missing, `grep` for it. If `grep` finds it, the problem is retrieval. If it finds nothing, the library
lacks it, and no retrieval change will help. If the guide was offered but not cited, the problem lies
with the prompt or the model.

### `compare`

Every question of the export, searched under several sets of retrieval settings, counted side by side.
Each variant is the bot's own query building (`RetrievalQueryComposer`), search and fusion with other
settings, so the numbers are what users would get. A follow-up (↩) is searched with its branch, as the
bot searches it.

Without `--variants` the columns are the built-in steps: **before** (the question's own words, no
country boost, every picture where it ranks, 8 excerpts), then **+context**, **+countries**,
**+headings** and **+planner** added one at a time at 8 excerpts, then the planner on top of the
defaults at 10 and 12 excerpts, and **shipped** — the defaults, which leave the planner off for the
request it costs, and which are also what a question gets when the planner is on but fails, times out
or sits out its pause. Without `--allow-chat` the planner is not asked, and the variants using it
search as the bot does when it fails.

`--variants` names a JSON file of other settings, each applied to the shipped defaults, or to the ones
before conversation-aware retrieval with `"base": "before"`. Keys are those of `AI:Retrieval`. Two are
the probe's own, for what the bot fixes in code: `"pixels"` re-weights the question-against-pixels
list and `"approximate": true` skips exact search.

```json
{ "country ×3": { "CountryBoost": { "Weight": 3 } },
  "12, planner": { "ExcerptLimit": 12, "QueryPlanner": { "Enabled": true } } }
```

Relevance cannot be counted without saying what relevant means, so it takes a targets file
(`--targets`). Each key is words that occur in a question; the value is a regex that an on-target
excerpt matches, or an object that also names the question's countries (an excerpt from elsewhere is
not on target), the excerpts known to answer it (matched against source URL and text, and placed in a
table of their own), and an indexed picture to stand in for an expired screenshot (📷):

```json
{ "khasi": "khasi|kesiya",
  "what about corn": { "match": "corn|maize", "countries": ["philippines"], "chunks": ["philippines#9hUv"] },
  "bollard a meta in australia": { "match": "bollard", "countries": ["australia"],
                                   "screenshot": "https://www.plonkit.net/netherlands#R6It" } }
```

`Targets/feedback-20261023.json` is the file for the 2026-10-23 export. Each cell reads **on-target
· in the question's country**; below the table come the totals, **pixel-only** excerpts (found by no
text search), **heading-only** pictures (whose only text is a guide heading, so the model can say
nothing about them), where each named excerpt was placed, and how long the planner took.

This is how the 2026-10-23 export's 49 questions came out, with `nemotron-3-ultra` planning. It was
measured before a follow-up stopped taking its branch's countries when it names countries of its own
without referring back, or asks where its screenshot was taken. Without the planner, that boosts no
question of the export differently; with it, only where the planner named a branch's country for such
a follow-up:

| | before | +context | +countries | +headings | +planner | +planner, 10 | +planner, 12 | shipped (10) |
|---|---|---|---|---|---|---|---|---|
| on-target | 187 of 392 (48 %) | 199 (51 %) | 227 (58 %) | 228 (58 %) | 224 (57 %) | 261 of 490 (53 %) | 303 of 588 (52 %) | 258 of 490 (53 %) |
| from the question's country | 72 % | 77 % | 96 % | 95 % | 96 % | 94 % | 93 % | 93 % |
| named excerpts offered | 7 of 24 | 11 | 13 | 14 | 14 | 16 | 17 | 14 |
| pixel-only | 44 | 39 | 13 | 12 | 2 | 4 | 4 | 23 |
| heading-only pictures | 29 | 21 | 24 | 9 | 15 | 18 | 21 | 14 |

The planner's two named excerpts cost a request per question. At the 45 requests a day the bot ships
with, that is 17 answers a day instead of 22, which is why it ships off.

The first export's 18 written questions, before the fusion weights changed, over 144 top-8 slots. The
production column is the bot as it ran then, at full pixel weight, without exact search or
de-duplication:

| | production then | pixels ×0.25, exact, de-duplicated | pixels ×0 |
|---|---|---|---|
| on-target | 73 | 88 | 97 |
| pixel-only | 53 | 13 | 0 |

### `grep`

The number of matching chunks, the guides they come from, a breakdown by country, and each match with
its context.

### `similarity`

For each chunk of the given guide it shows three numbers:

- **stored**: the index's own score for the chunk against the question;
- **rank**: the chunk's place among the question's results;
- **rebuilt**: the chunk's text put back through the ingestion's `EmbeddingTextBuilder` and embedded
  now.

Each `--variant` is built the same way, differing from the chunk in its text alone, and gets a
similarity and the rank it *would* take. Compare variants against **rebuilt**, not **stored**: both
are embedded now and the same way, while the stored vector may predate a change to the recipe.

This is how link URLs were found to hurt. With the URLs stripped, "white car long antenna" went from
0.590 to 0.646, which moves it from about 39th to 8th.

### `answer`

`replay` says what an answer would be *offered* today. `answer` asks it: every rated answer of an
export goes through the bot's answering code again — `AskAiHandler` with its real collaborators, the
`OpenRouterEmbedder`, the `QdrantKnowledgeIndex` behind the read-only interceptor, the
`RefitChatModelClient` and `ChatModelCatalog` with today's roster and models' health, the tool loop
(`AiAnswerLoop`, `AnswerToolbox`) and the `GbifSpeciesRangeReader` — and the report puts the new answer,
rendered by the bot's own `AiAnswerFormatter`, beside the one the member rated.

```bash
# Everything, with the tools on as they ship, and a model's verdict on each.
dotnet run --project Tools/GeoClubBot.RetrievalProbe -- answer ai_feedback/<export>.jsonl \
    --allow-chat --judge --out ai_feedback/eval/answer.md

# The answers rated since the first export, without tools.
dotnet run --project Tools/GeoClubBot.RetrievalProbe -- answer ai_feedback/<export>.jsonl \
    --allow-chat --judge --only after:2026-09-27 --set AI:Tools:Enabled=false --out ai_feedback/eval/no-tools.md
```

**Settings.** The bot's `GeoClubBot.API/appsettings.json`, bound into its configuration classes as its
host binds them, with `--set` overrides on top (`AI:…` keys only; `AI__X__Y` spelling works too) and
validated as the bot validates them at start-up. Not its development settings, which pick other models
and another collection, and not the environment, so a variable left set in a shell cannot change a
measurement unseen. Production's own environment overrides are not applied either: name them with
`--set` to measure them.

**What is not the bot's.** No Discord: nothing is posted or stored, and a relayed picture is listed by
its link where production would upload it from its disk. The conversation store holds one branch,
rebuilt from the export with made-up message ids and dated a minute apart just before now, so a
follow-up is answered with its branch, and offered again the excerpts the answer before it cited
where the export kept their chunk ids (exports from before 2026-10-25 did not). The allowance never
runs out and starts afresh for every answer, as the first question of a day. The bot's HTTP pipeline
lives in the API project, so `PacedRetryHandler` stands in for it: requests paced to
`AI:OpenRouter:PerMinuteRequestBudget`, each attempt bounded by `AI:RequestTimeoutSeconds`, transient
failures retried twice and a 429 once after its minute, as the bot retries them. The catalog is refreshed
once per run — its failure penalties then carry from one answer to the next, as in the bot.

**Screenshots.** A Discord attachment link carries its expiry (`ex`) and is dead after it; a model asked
to read one fails the whole request. Such a screenshot is left out — the question asked by its text
alone, and the report says so (📷). A judge is told, since the rated answer could see it.

**Cost and the cache.** An answer costs what it costs the bot: one embedding and one chat request, plus a
chat request per tool round and an embedding per round that searches — six at most with the default two
rounds, and one more when the call after them is asked again without tools (a reply that was not an
answer, such as a tool call written out as text).
Every run also reads the roster and up to `HealthCheckModelCount` models' health (free `GET`s). Each
answer is cached in `answer-cache/` next to the binary, keyed by the feedback record, a fingerprint of the
settings, and the build of the bot's Application and Infrastructure assemblies, so a rerun asks only what
failed or what a code or settings change made new. Failures are never cached: after a 429 or a 504, run
the same command again later and it resumes. Verdicts are cached by the exact prompt and judge chain.

**The report.** A summary — the judge's verdicts overall and for 👎, how many commented answers it says
are addressed, hallucination risk, tool calls by tool, answers citing a picture, latency, requests —
and a table with a row per record. Then per record: the branch and question, the rating and comment,
the old answer and what it cited, the new answer as Discord would post it (citations resolved, the
numbered source list, the model), its pictures and where they come from, its tool calls (arguments,
outcome, result size, time), the guides offered now against then (✓ cited, ★ new), each chat request
(chain, tools, the model that answered, finish reason, tokens, time), and the bot's log.

**The judge** (`--judge`) is given the branch, the question, the member's rating and comment, both
answers — the new one as the model wrote it, its numbers those of the excerpts — the pictures the new
answer attaches, and the excerpts and tool results it was written from. It replies
`{verdict: better|same|worse, addressesComment: true|false|null, hallucinationRisk: low|medium|high, reasons}`.
Its verdicts are **indicative**: one free model's reading, blind to pictures, possibly the model that
wrote the new answer, and not the member who rated the old one. Read the answers too.

## Adding a command

1. Add a `XxxCommand.cs` with `public static Task RunAsync(ProbeContext context)`, writing its report
   through `context.Report`.
2. Add it to the command switch in `Program.cs`, to `ProbeArguments.PrintUsage()`, and to the table
   above.
3. Keep it read-only. If it needs a Qdrant call that is not on `ReadOnlyQdrantInterceptor`'s
   allow-list, add the call only if it reads. Anything that writes belongs in the bot, behind a use
   case, not in this tool.
