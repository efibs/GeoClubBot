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
- **Snapshot** (`Discord/*FormatterTests`, fast): `Verify.Xunit` captures whole rendered messages
  into committed `*.verified.txt` files. To update after an intended change, run the test, inspect
  the new `*.received.txt`, and replace the `*.verified.txt` (or use a Verify diff tool). `*.received.*`
  is gitignored.
- **Mutation** (`stryker-config.json`, manual/nightly): `dotnet stryker` mutates the Application
  project and runs the full suite to measure how effectively tests catch bugs. Not on the PR gate
  (see `.github/workflows/mutation.yml`); `break: 0` so it reports without failing. Run locally with
  `dotnet tool restore && dotnet stryker`.
- **Property-based** (`PropertyBased/`, fast): `CsCheck` asserts invariants of the pure logic
  (`TimeRange` algebra; `DateTimeOffset` `Truncate`/`RoundUp` windowing) over thousands of random
  inputs and shrinks failures to a minimal counterexample. Generate timestamps at UTC (offset zero)
  and leave tick head-room below `DateTimeOffset.MaxValue` so adding intervals can't overflow.

### Local dev without real credentials

Set `GeoGuessr:UseMock=true` (default in `appsettings.Development.json`) to run against the
in-process **GeoClubBot.MockGeoGuessr** instead of the real GeoGuessr API. It serves a mock API
plus a UI (URL logged at startup) for seeding/driving fake club data.

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
| **GeoClubBot.Infrastructure** | EF Core DbContext + repositories, Quartz scheduled jobs, SignalR hub |
| **GeoClubBot.Discord** | Discord.Net interaction modules (slash commands), Discord output adapters |
| **Configuration** | Strongly-typed config classes with `IValidateOptions` |
| **Constants** | Config keys, string constants, component IDs |
| **Extensions** | Helper extension methods |
| **Utilities** | General utilities |
| **QuartzExtensions** | `ConfiguredCronJobAttribute` for declarative cron job registration |
| **GeoClubBot.MockGeoGuessr** | In-process fake GeoGuessr API + Razor UI for local dev (gated by `GeoGuessr:UseMock`) |
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
- **Result type**: Use cases return `Result<T>` / `Error` (`Utilities/Result.cs`) instead of throwing for expected failures. `Error.Type` (`ErrorType.NotFound`, `Validation`, `Conflict`, `Forbidden`, `Unauthorized`, `Unexpected`) is mapped to HTTP status codes by the `ResultExtensions` middleware in `GeoClubBot.API/Middleware/`.
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
- **Discord** (Discord.Net 3.18.0): bot token, slash commands, role/channel management
- **Qdrant + Semantic Kernel** (optional): AI features toggled via `AI:Active` config flag

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
