# GeoClubBot.RetrievalProbe

A **read-only** console tool that replays questions through the bot's own retrieval against a
knowledge index, which is normally production's.

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
   run. The one exception has to be asked for by name: with `--allow-chat`, the query planner's
   `POST /api/v1/chat/completions` and the `GET /api/v1/models` roster it picks a model from may leave
   too — through a second handler built for the planner alone. The embedder's handler never lets a chat
   request through, flag or not.
3. **No verb in the CLI.** Every command reads. There is no option that writes.
4. **A cache.** Question vectors are kept in `embedding-cache.json` and planner replies in
   `planner-cache.json`, next to the built binary, so rerunning a report costs nothing. Vectors are
   keyed by embedding model; planner replies by exactly what was sent (and by the chain, when
   `--planner-models` names one).

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

| Option | Meaning |
|---|---|
| `--qdrant <url>` | Qdrant's gRPC address |
| `--collection <name>` | Collection to read (default: the bot's own) |
| `--limit <n>` | Excerpts per question (default: `AI:Retrieval:ExcerptLimit`'s default, as the bot offers) |
| `--rating good\|bad` | `replay`/`compare`: only answers rated this way |
| `--targets <file>` | `replay`/`compare`: count on-target excerpts, place known answers, stand in for expired screenshots (see below) |
| `--allow-chat` | `replay`/`compare`: let the query planner's chat requests leave the probe — one per question, cached. `replay` then searches with the planner on (it ships off); without it, `compare`'s variants that plan search as the bot does when the planner fails |
| `--planner-models <ids>` | `replay`/`compare`: comma-separated chain the planner asks instead of the bot's own, to measure another model; cached apart |
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

## Adding a command

1. Add a `XxxCommand.cs` with `public static Task RunAsync(ProbeContext context)`, writing its report
   through `context.Report`.
2. Add it to the command switch in `Program.cs`, to `ProbeArguments.PrintUsage()`, and to the table
   above.
3. Keep it read-only. If it needs a Qdrant call that is not on `ReadOnlyQdrantInterceptor`'s
   allow-list, add the call only if it reads. Anything that writes belongs in the bot, behind a use
   case, not in this tool.
