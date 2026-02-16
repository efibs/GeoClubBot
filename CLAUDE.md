# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GeoClubBot is a .NET 10.0 ASP.NET Core Web API + Discord bot for managing GeoGuessr gaming clubs. It integrates with the GeoGuessr API and Discord to track member activity, manage strikes, run daily challenges, and handle account linking.

## Build & Run Commands

```bash
# Build
dotnet build GeoClubBot.sln

# Run the API (entry point project)
dotnet run --project GeoClubBot.API

# Start local PostgreSQL + Qdrant via Docker
docker compose up postgresqldb

# Build Docker image
docker build -f ./GeoClubBot.API/Dockerfile -t ghcr.io/efibs/geo-club-bot:dev .

# Add a new EF Core migration
dotnet ef migrations add <MigrationName> --project GeoClubBot.Infrastructure --startup-project GeoClubBot.API
```

There are no test projects in this repository.

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

### Key Patterns

- **Unit of Work**: `IUnitOfWork` (in Application) / `DbUnitOfWork` (in Infrastructure) aggregates all repositories. All data access goes through `IUnitOfWork`.
- **Use Cases**: Each use case has an input port interface (`IXxxUseCase`) in `Application/InputPorts/` and implementation in `Application/UseCases/`. Registered as transient in `ClubBotServices.cs`.
- **Domain Events**: `BaseEntity` collects domain events; `GeoClubBotDbContext.SaveChangesAsync` dispatches them via MediatR.
- **Refit HTTP Client**: `IGeoGuessrClient` is a declarative Refit interface for the GeoGuessr API, with Polly resilience (rate limiting, retry, circuit breaker) configured in `ResiliencePipelines.cs`.
- **Quartz Jobs**: Jobs use `[ConfiguredCronJob("ConfigKey:Schedule")]` attribute for auto-discovery. Located in `Infrastructure/InputAdapters/Jobs/`.
- **Discord Interactions**: Slash command modules in `Discord/InputAdapters/Interactions/`. Output adapters in `Discord/OutputAdapters/` implement interfaces from `Application/OutputPorts/Discord/`.

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
- File-scoped namespaces
- No `.editorconfig` or `Directory.Build.props` present