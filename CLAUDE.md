# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GeoClubBot is a .NET 10.0 ASP.NET Core Web API + Discord bot for managing GeoGuessr gaming clubs. It integrates with the GeoGuessr API and Discord to track member activity, manage strikes, run daily challenges, and handle account linking.

> **New to the codebase?** See [`Documentation/DeveloperGuide.md`](Documentation/DeveloperGuide.md) for a solution map, the namespace↔folder gotcha, and step-by-step recipes answering "where do I add a slash command / use case / repository / job?".

## Build & Run Commands

```bash
# Build
dotnet build GeoClubBot.sln

# Run the API (entry point project)
dotnet run --project GeoClubBot.API

# Start local PostgreSQL (compose.yaml also defines a `qdrant` service for AI features)
docker compose up postgresqldb

# Build Docker image
docker build -f ./GeoClubBot.API/Dockerfile -t ghcr.io/efibs/geo-club-bot:dev .

# Add a new EF Core migration
dotnet ef migrations add <MigrationName> --project GeoClubBot.Infrastructure --startup-project GeoClubBot.API
```

### Tests

Tests live in **GeoClubBot.Tests** (xUnit + FluentAssertions + NSubstitute).

```bash
# Run all tests
dotnet test GeoClubBot.Tests/GeoClubBot.Tests.csproj

# Run a single test by name (or substring)
dotnet test --filter "FullyQualifiedName~CheckStrikeDecayHandlerTests"

# Run only fast unit tests (exclude integration tests that need Docker)
dotnet test --filter "Category!=Integration"
```

Integration tests (`Integration/`, `[Trait("Category", "Integration")]`) spin up a real
PostgreSQL via **Testcontainers** — they require a running Docker daemon. They share one
container (`PostgresCollection`/`PostgresFixture`) and each test namespaces its own seed data
by random `Club`/`UserId` so the container is reused safely.

A second Testcontainers fixture (`QdrantCollection`/`QdrantFixture`) backs the Qdrant vector-index
tests. `QdrantClient`'s methods are sealed interface implementations, so NSubstitute can't fake
them — a real container is the only way to cover `QdrantKnowledgeIndex`. Each test takes a
unique collection name via `QdrantFixture.NewCollectionName()` instead of namespacing rows, and the
fixture raises the container's file-descriptor limit: Qdrant opens many RocksDB files per collection
and per payload index, and the default limit is exhausted part-way through a run.

**Test types beyond unit + integration:**

- **End-to-end** (`Integration/E2E/`, also `Category=Integration`): `GeoClubBotApiFactory` boots
  the real API in-process via `WebApplicationFactory<Program>` against the Postgres container,
  exercising routing → controller → middleware → EF. The factory strips background hosted services
  (Discord gateway, `InitialSyncService`, Quartz) so the host starts cleanly, and re-registers the
  `DbContext` against the test container. `Program` is exposed via `public partial class Program`.
- **Architecture** (`Architecture/`, fast): `NetArchTest` rules enforce the Clean Architecture
  boundaries (Domain/Application don't depend on outer layers, EF stays behind a port). Use
  fully-qualified namespaces in rules — NetArchTest matches string constants, so a bare
  `GeoClubBot` token false-flags the `"GeoClubBot.Application"` meter-name literals.
- **Discord module loading** (`Discord/InteractionModuleLoadingTests`, fast): loads every interaction
  module into an `InteractionService` and checks Discord's name/description limits. No other test does
  this (the E2E host strips the gateway), so a broken module would otherwise only show at bot start-up.
  Tests that set the static `ConfiguredCronJobAttribute.Config` share the non-parallel
  `ConfiguredCronJobCollection`.
- **Snapshot** (`Discord/*FormatterTests`, `Application/UseCases/CountryChallenges/CountryChallengeMessagesTests`,
  fast): `Verify.Xunit` captures whole rendered messages
  into committed `*.verified.txt` files. To update after an intended change, run the test, inspect
  the new `*.received.txt`, and replace the `*.verified.txt` (or use a Verify diff tool). `*.received.*`
  is gitignored.
- **Mutation** (`stryker-config.json`, manual/nightly): `dotnet stryker` mutates the Application
  project and runs the full suite to measure how effectively tests catch bugs. Not on the PR gate
  (see `.github/workflows/mutation.yml`); `break: 0` so it reports without failing. Run locally with
  `dotnet tool restore && dotnet stryker`.
- **Property-based** (`PropertyBased/`, fast): `CsCheck` asserts invariants of the pure logic
  (`TimeRange` algebra; `DateTimeOffset` `Truncate`/`RoundUp` windowing; the AI content chunker, whose
  chunk keys must stay stable or every re-ingest duplicates instead of updating; the AI citation
  resolver, whose numbers must run 1..k and all lead somewhere; the retrieval fusion, which must never
  offer the same text twice; the country pool rotation, where every round must be a permutation of the
  pool; the leaderboard ranking, where ties share a rank) over thousands of
  random inputs and shrinks failures to a minimal counterexample. Generate timestamps at UTC (offset zero)
  and leave tick head-room below `DateTimeOffset.MaxValue` so adding intervals can't overflow.

### Local dev without real credentials

Set `GeoGuessr:UseMock=true` (default in `appsettings.Development.json`) to run against the
in-process **GeoClubBot.MockGeoGuessr** instead of the real GeoGuessr API. It serves a mock API
plus a UI (URL logged at startup) for seeding/driving fake club data. The club view's *Add
Activity* form picks the activity kind (daily challenge or duel / board mission / board bonus /
club challenge, plus the legacy daily and weekly missions), which is how you exercise the
same-priced 20 XP awards locally. The *Mission Board* view drives each club's weekly board —
claim, request help, help out, complete, backdate a claim to make it look stuck, start a new week —
and completing a mission logs the type-5 (and board-clear type-6) feed entries GeoGuessr would.
The mock client only has a board when created per club (`CreateClient(clubId)`), like a club token.

> The whole mock UI is one embedded file, `GeoClubBot.MockGeoGuessr/wwwroot/mock.html` (plain HTML
> + fetch against `/mock/api`). A duplicate Blazor version of it once existed but was never routed;
> it has been deleted.

### Inspecting the real GeoGuessr API

When you need to know what GeoGuessr *actually* returns — the typed DTOs silently drop fields they
don't declare — use the read-only probe instead of guessing or instrumenting the bot:

```bash
dotnet run --project Tools/GeoClubBot.ApiProbe -- activities --pages 3
```

It prints raw JSON plus a field census (every property, its distinct values, and a cross-tab
against `xpReward`). It only ever issues GETs, and it needs an `_ncfa` token —
see [`Tools/GeoClubBot.ApiProbe/README.md`](Tools/GeoClubBot.ApiProbe/README.md).

### Measuring the AI's retrieval

When a feedback export (`/ai feedback-export`) shows bad answers, or before changing retrieval, replay
the rated questions through the bot's own search against the real index instead of guessing:

```bash
dotnet run --project Tools/GeoClubBot.RetrievalProbe -- replay ai_feedback/<export>.jsonl --out ai_feedback/replay.md
```

`replay` shows what each question is offered and where every excerpt came from. `compare` counts
alternative fusion weights; `grep` answers "is it in the index at all?"; `similarity` tries a re-worded
chunk. It reaches production Qdrant over an SSH tunnel to the gRPC port, only ever reads (a gRPC
allow-list, tested against a real Qdrant), and caches the question embeddings it pays for — see
[`Tools/GeoClubBot.RetrievalProbe/README.md`](Tools/GeoClubBot.RetrievalProbe/README.md).

## Architecture

The codebase follows **Clean Architecture** with ports-and-adapters:

```
Domain (entities, domain events)
  ↓
Application (use cases, input/output port interfaces)
  ↓
Infrastructure (EF repos, Quartz jobs, Discord adapters)
  ↓
API + Discord (controllers, slash command modules)
```

### Solution Projects

| Project | Role |
|---|---|
| **GeoClubBot.API** | ASP.NET Core host, DI setup, controllers, Program.cs entry point |
| **GeoClubBot.Domain** | Entities (Club, ClubMember, GeoGuessrUser, etc.) with domain events via MediatR |
| **GeoClubBot.Application** | Use cases as MediatR handlers; input ports (use case interfaces) and output ports (repository/service interfaces) |
| **GeoClubBot.Infrastructure** | EF Core DbContext + repositories, Quartz scheduled jobs, SignalR hub, AI adapters (`OutputAdapters/AI/`) |
| **GeoClubBot.Discord** | Discord.Net interaction modules (slash commands), Discord output adapters |
| **Configuration** | Strongly-typed config classes validated with `.ValidateDataAnnotations().ValidateOnStart()` (the one `IValidateOptions` implementation, `ActivityRulesOptionsValidator`, lives in Application because it needs the activity-kind enum) |
| **Constants** | Config keys, string constants, component IDs |
| **Extensions** | Helper extension methods |
| **Utilities** | General utilities |
| **QuartzExtensions** | `ConfiguredCronJobAttribute` for declarative cron job registration |
| **GeoClubBot.MockGeoGuessr** | In-process fake GeoGuessr API + single-page HTML UI for local dev (gated by `GeoGuessr:UseMock`) |
| **GeoClubBot.Tests** | xUnit unit + Testcontainers-backed integration tests |

> **Namespace gotcha**: assembly names are `GeoClubBot.*` but several projects set a short
> `RootNamespace` — Application → `UseCases`, Domain → `Entities`, Infrastructure → `Infrastructure`.
> `Configuration`, `Constants`, `Extensions`, `Utilities`, `QuartzExtensions` use their own names;
> only `GeoClubBot.Discord` keeps the `GeoClubBot.` prefix. Match the existing `using`s, not the folder.

### Key Patterns

- **Use Cases**: Each use case is a MediatR request (`ICommand`/`IQuery` record, from `Application/Abstractions/`) plus an `IRequestHandler<,>` handler, co-located per feature in `Application/UseCases/<Feature>/` (optional FluentValidation validators under `Validators/`). Handlers and validators are **auto-registered** via assembly scan (`IUseCasesAssemblyMarker`) in `Program.cs` — no manual DI.
- **Repositories**: Output-port interfaces (`IXxxRepository`) live in `Application/OutputPorts/Repositories/`; EF implementations (`EfXxxRepository`) in `Infrastructure/OutputAdapters/Repositories/`, registered in `PersistenceModule`. Handlers inject the repository interfaces directly.
- **Unit of Work**: `IUnitOfWork` / `DbUnitOfWork` exposes only `SaveChangesAsync()` (it does **not** aggregate repositories); the MediatR `UnitOfWorkBehavior` calls it to commit after each command.
- **Domain Events**: `BaseEntity` collects domain events; `GeoClubBotDbContext.SaveChangesAsync` dispatches them via MediatR.
- **Refit HTTP Client**: `IGeoGuessrClient` is a declarative Refit interface for the GeoGuessr API, with Polly resilience (rate limiting, retry, circuit breaker) configured in `ResiliencePipelines.cs`.
- **Quartz Jobs**: Jobs use `[ConfiguredCronJob("ConfigKey:Schedule")]` attribute for auto-discovery. Located in `Infrastructure/InputAdapters/Jobs/`.
- **Discord Interactions**: Slash command modules in `Discord/InputAdapters/Interactions/<Feature>/` (feature subfolders mirroring `Application/UseCases/`), auto-discovered via `InteractionsAssemblyMarker` — no manual registration. Output adapters in `Discord/OutputAdapters/` implement interfaces from `Application/OutputPorts/Discord/`.
- **Club XP activity kinds**: GeoGuessr's club activity feed labels each entry with a numeric
  `type`: 4 = daily challenge / duel (the streak, 20 XP), 5 = board mission (20 XP, credited to the
  **claimer** only), 6 = board-clear bonus (100 XP, to whoever finished the board's last mission),
  3 = club challenge (0 XP). Types 1/2 (daily/weekly mission) ended on 2026-09-23. Several sources
  share an XP amount, so `ClubActivityKindClassifier` (`Application/OutputPorts/GeoGuessr/`) is the
  only place that decides; call `IsDailyChallenge` / `IsBoardMission` / `IsBoardClearBonus` rather
  than comparing `XpReward`. `ClubXp:UntypedXpFallback` only maps untyped entries (empty by default).
- **Club mission board**: `GET /v4/missions/club/board` (and `/previous`) has no club id — it answers
  for the club of the token's account, so it is always read through `CreateClient(clubId)` with that
  club's own `NcfaToken`, via the cached `IClubMissionBoardReader` (it warns when no claimer belongs to
  the club). Helpers on a mission are only user ids of whoever pressed "help out" — nothing proves they
  contributed, so helps never count towards anything and are shown as unverified. The claim day comes
  from the board's `you.nextDayAt` (`MissionBoard:ClaimResetTime*` is only the fallback).
- **Weekly activity rules**: the check reads the activity feed for its window (last check → now,
  capped by `ActivityChecker:MaxFeedLookback`) and judges it with `ActivityRules`/`ActivityRuleEvaluator`
  (`Application/UseCases/ClubMemberActivity/Rules/`): per-kind minimum counts (`Requirements`, e.g.
  6 streak days + 2 board missions) plus an optional minimum **rule XP** (`RuleXp`: excluded kinds and
  per-week caps, so the board bonus and missions beyond 3 don't count). Targets scale down for members
  who joined mid-week or were excused. Each new history snapshot records the interval's rule XP, and
  averages (report, dashboard leaderboard, swap suggestions, MVP) use it, falling back to the raw XP
  difference for older snapshots. Kind names in config are validated at start-up.
- **Result type**: Use cases return `Result<T>` / `Error` (`Utilities/Result.cs`) instead of throwing for expected failures. `Error.Type` (`ErrorType.NotFound`, `Validation`, `Conflict`, `Forbidden`, `Unauthorized`, `Unexpected`) is mapped to HTTP status codes by the `ResultExtensions` middleware in `GeoClubBot.API/Middleware/`.
- **AI assistant** (optional, `AI:Active`): retrieval-then-generation rather than tool-calling, because
  requiring tool support would exclude most free models. Ports live in `Application/OutputPorts/AI/`
  (`IChatModelClient`, `IEmbedder`, `IKnowledgeIndex`, `ISourceExtractor`), adapters in
  `Infrastructure/OutputAdapters/AI/`. Conversations are a tree of Discord reply edges, so sibling
  branches stay independent. Chat services and the vector index are registered **even when the feature
  is off** — MediatR's assembly scan picks up every Application handler regardless, so the container
  must be able to construct their dependencies or start-up validation fails. Guide images from hosts
  that refuse unattended clients are copied during indexing and served from
  `/api/v1/ai/images/{hash}` — content-addressed, anonymous, and strictly not a proxy — but are sent
  to the embedder **inline**, so indexing never depends on the provider reaching this host. In the
  OpenRouter resilience pipeline, retry must stay *outside* the rate limiter (every attempt takes a
  token); `OpenRouterResiliencePipelineTests` pins this. The fallback router `openrouter/free` picks a
  **random** free model (safety classifiers and 2B models included) and cannot exclude any, so chains
  reach it only when too few vetted models qualify; every answer is screened
  (`GuardrailVerdictDetector`, `IChatModelCatalog.IsUnfitToAnswer`) and a failed or unusable first
  chain is retried once against untried models. Model citations are rewritten by `CitationResolver`
  into one `[n]` numbering, pictures included. Retrieval fuses its per-vector searches client-side
  (`KnowledgeHitFusion`: weighted RRF plus collapsing of identical texts) because Qdrant 1.15 cannot
  weight RRF; the weights were measured on production — see the guide before changing them. Answers
  can be rated (👍/👎 reaction, or a message context menu for a written comment); a rated conversation
  is **copied** into `AiAnswerFeedbacks`/`AiFeedbackTurns`, which the conversation retention sweep
  never touches — that copy is the only permanently stored conversation. See
  [`Documentation/AiGuide.md`](Documentation/AiGuide.md).
- **Country challenges** (optional, `CountryChallenges:Enabled`): themed weekday challenges configured in
  a hand-edited JSON file (`CountryChallengesConfig.example.json`) that is re-read on every run.
  `CountryChallengePlanResolver` fills inherited values (built-in → file → challenge → pool entry) and
  reports every problem with its path; a disabled challenge's problems are only warnings. Unknown JSON
  properties are errors, never ignored. `CountryChallengeMessages` renders every text for both the run and
  `/country-challenges-admin preview`, so keep them on one path. Allowed mentions are taken from the raw
  templates, never the rendered text, so GeoGuessr nicknames cannot ping. Each run phase commits before
  posting, and each is idempotent per day (unique `(ChallengeName, Date, Country)`, since `Picks` lets a challenge play several countries a day; `EvaluatedAt`; one leaderboard
  post per date), which is what makes `post-now` safe to repeat. `results-now` evaluates every pending challenge ahead of
  its due day; it and the runs share `CountryChallengeRunLock`. `ConfiguredCronJobAttribute` takes an
  optional time-zone key for this job; every other job stays on UTC. See
  [`Documentation/CountryChallengesGuide.md`](Documentation/CountryChallengesGuide.md).
- **Observability**: OpenTelemetry traces + metrics (custom meters like `HandlerMetrics`). The OTLP exporter is opt-in via the `OpenTelemetry:Endpoint` config key; absent that, telemetry stays in-process. Wired in `Program.cs`.

### DI Registration

- Config options: `Configuration/DependencyInjectionExtensions.cs` → `AddClubBotOptions()`
- Discord services: `GeoClubBot.Discord/DependencyInjection/DiscordServices.cs` → `AddDiscordServices()`
- All other services: `GeoClubBot.API/DependencyInjection/ClubBotServices.cs` → `AddClubBotServices()`
- MediatR: registered from the use cases assembly via `IUseCasesAssemblyMarker`

### Database

- PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`
- DbContext: `GeoClubBotDbContext` in `Infrastructure/OutputAdapters/DataAccess/`
- Migrations auto-apply on startup when `SQL:Migrate` is `true`
- Connection string key: `ConnectionStrings:PostgresDb`

### External Integrations

- **GeoGuessr API** (`https://www.geoguessr.com/api`): authenticated via `_ncfa` cookie token
- **Discord** (Discord.Net 3.20.1): bot token, slash commands, role/channel management
- **OpenRouter** (optional): chat *and* embeddings for the AI assistant, with the free model chosen
  automatically from whatever is available that day. The only external AI dependency.
- **Qdrant** (optional): vector store for indexed guide content, using named `text`/`image` vectors
  merged with reciprocal-rank fusion. Both are gated behind the `AI:Active` flag — see
  [`Documentation/AiGuide.md`](Documentation/AiGuide.md), which also explains the free-tier request
  allowance that shapes most of the design.

## C# Conventions

- .NET 10.0, C# 14, nullable reference types enabled, implicit usings enabled
- Conventions are enforced by `.editorconfig` (no `Directory.Build.props`): file-scoped namespaces, `using` directives **outside** the namespace, `_camelCase` private fields, 4-space indent (2 for JSON/YAML), Allman braces, `var` when the type is apparent.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
