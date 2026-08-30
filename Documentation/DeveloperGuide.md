# Developer Guide — navigating GeoClubBot

This is the **"where does X go?"** guide for the GeoClubBot solution. It complements
[`CLAUDE.md`](../CLAUDE.md) (architecture overview, build/test commands) and
[`ResultConventions.md`](ResultConventions.md) (error handling).

> TL;DR: features are **vertical slices**. The code for one feature is split across
> layers by design (Clean Architecture), but within each layer it lives in a
> **folder named after the feature**. Find the slice folder, and everything for that
> feature is one of its siblings.

---

## Solution map

| Project | What lives here |
|---|---|
| **GeoClubBot.API** | ASP.NET Core host, `Program.cs`, controllers, **DI composition root** (`DependencyInjection/`) |
| **GeoClubBot.Discord** | Discord.Net slash-command modules (`InputAdapters/Interactions/`) + Discord output adapters (`OutputAdapters/`) |
| **GeoClubBot.Application** | Use cases (`UseCases/<Feature>/`) and port interfaces (`OutputPorts/`). The heart of the app |
| **GeoClubBot.Domain** | Entities + domain events. No framework dependencies |
| **GeoClubBot.Infrastructure** | EF Core repositories (`OutputAdapters/Repositories/`), Quartz jobs (`InputAdapters/Jobs/`), DbContext, SignalR |
| **Configuration** | Strongly-typed `*Configuration` option classes |
| **Constants** | `ConfigKeys`, component IDs, string constants |
| **QuartzExtensions** | `[ConfiguredCronJob]` attribute + assembly scanning for jobs |
| **Extensions / Utilities** | Small shared helpers (`Result<T>` lives in `Utilities`) |
| **GeoClubBot.MockGeoGuessr** | In-process fake GeoGuessr API for local dev (`GeoGuessr:UseMock=true`) |
| **GeoClubBot.Tests** | xUnit unit + Testcontainers integration tests |

### "I want to change X → go here"

| Goal | Start here |
|---|---|
| Add / edit a **slash command** | `GeoClubBot.Discord/InputAdapters/Interactions/<Feature>/` + a use case in `GeoClubBot.Application/UseCases/<Feature>/` |
| Add a **use case** (business operation) | `GeoClubBot.Application/UseCases/<Feature>/` |
| Add a **repository** (data access) | interface in `GeoClubBot.Application/OutputPorts/Repositories/`, impl in `GeoClubBot.Infrastructure/OutputAdapters/Repositories/` |
| Add a **scheduled job** | `GeoClubBot.Infrastructure/InputAdapters/Jobs/` |
| Add a **config option** | `Configuration/<Feature>Configuration.cs` |
| Add an **entity / domain event** | `GeoClubBot.Domain/` (events under `Events/`) |
| Change **error → user message** mapping | see [`ResultConventions.md`](ResultConventions.md) |

---

## Namespace ↔ folder gotcha (read this once)

Several projects set a **short `RootNamespace`** that does **not** match the assembly /
folder name. Match the existing `using`s, not the directory:

| Project (folder) | RootNamespace | Example file → namespace |
|---|---|---|
| `GeoClubBot.Application` | `UseCases` | `UseCases/Strikes/AddStrikeCommand.cs` → `UseCases.UseCases.Strikes` |
| `GeoClubBot.Domain` | `Entities` | `Club.cs` → `Entities` |
| `GeoClubBot.Infrastructure` | `Infrastructure` | `OutputAdapters/Repositories/EfClubRepository.cs` → `Infrastructure.OutputAdapters.Repositories` |
| `GeoClubBot.Discord` | *(default)* | `InputAdapters/Interactions/Club/ClubStatsModule.cs` → `GeoClubBot.Discord.InputAdapters.Interactions.Club` |
| `GeoClubBot.API` | `GeoClubBot` | `DependencyInjection/ClubBotServices.cs` → `GeoClubBot.DependencyInjection` |

Yes — `UseCases.UseCases.<Feature>` (doubled) is correct and intentional for the
Application project.

---

## What is auto-wired (don't register it manually)

The codebase leans on assembly scanning. **You do not register these by hand:**

| Thing | How it's discovered | Marker / mechanism |
|---|---|---|
| **MediatR handlers** (`IRequestHandler<,>`) | assembly scan in `Program.cs` | `IUseCasesAssemblyMarker` |
| **FluentValidation validators** (`AbstractValidator<>`) | assembly scan in `Program.cs` | `IUseCasesAssemblyMarker` |
| **Discord modules** (`: ClubBotInteractionModule`) | `InteractionService.AddModulesAsync(assembly)` | `InteractionsAssemblyMarker` (`InteractionHandler.cs`) |
| **Quartz jobs** (`: IJob` + `[ConfiguredCronJob]`) | `q.AddCronJobs(assembly)` | `JobAssemblyMarker` (`QuartzModule.cs`) |

**What you still register by hand** (intentional — these are explicit composition seams):

- **Repositories / output-port adapters** → `GeoClubBot.API/DependencyInjection/Modules/`
  (`PersistenceModule` for repos, `DiscordAdaptersModule` for Discord adapters, etc.)
- **Config options** → `Configuration/DependencyInjectionExtensions.cs`

---

## Recipes

### 1. Add a Discord slash command

A command is a thin Discord adapter that sends a MediatR request into the Application layer.

1. **Use case** — in `GeoClubBot.Application/UseCases/<Feature>/`:
   - a request record implementing `IQuery<Result<T>>` (read) or
     `ICommand<Result<T>>` / `ICommand` (write — `ICommand` returns `Unit`), and
   - a handler implementing `IRequestHandler<TRequest, TResponse>`.
   - *(Auto-wired — no DI edit.)* Return `Result<T>` for expected failures
     (see [`ResultConventions.md`](ResultConventions.md)).
2. **(Optional) validator** — `UseCases/<Feature>/Validators/<Request>Validator.cs`
   extending `AbstractValidator<TRequest>`. *(Auto-wired.)*
3. **Module** — in `GeoClubBot.Discord/InputAdapters/Interactions/<Feature>/`:
   create or extend a class that `: ClubBotInteractionModule(mediator, logger)`,
   add a `[SlashCommand(...)]` method that calls `Mediator.Send(...)` inside the
   `ExecuteAsync(...)` helper. *(Auto-discovered — no DI edit, no manual command
   registration.)*

Model it on `Interactions/Users/UserInfoModule.cs` or `Interactions/Club/ClubStatsModule.cs`.
**Folder choice:** put the module in the subfolder matching the use-case slice. If you only
need a new subcommand on an existing group, add a method to that group's existing module
instead of a new file.

`scripts/new-usecase.sh` scaffolds steps 1–3 for you (see [Scaffolding](#scaffolding) below).

### 2. Add a use case (no Discord command)

Same as step 1 above, minus the module — e.g. a use case triggered by a job or a
domain event. The handler is auto-registered.

### 3. Add a repository / output port

1. **Interface** → `GeoClubBot.Application/OutputPorts/Repositories/I<Name>Repository.cs`
   (namespace `UseCases.OutputPorts.Repositories`).
2. **Implementation** → `GeoClubBot.Infrastructure/OutputAdapters/Repositories/Ef<Name>Repository.cs`
   (namespace `Infrastructure.OutputAdapters.Repositories`), taking
   `GeoClubBotDbContext` via the constructor.
3. **Register it** → add one line to
   `GeoClubBot.API/DependencyInjection/Modules/PersistenceModule.cs`:
   `services.AddTransient<I<Name>Repository, Ef<Name>Repository>();`

Handlers inject the repository interface directly via the constructor.
**Note:** `IUnitOfWork` only exposes `SaveChangesAsync()` — it does **not** aggregate
repositories, so there's nothing to add there.

### 4. Add a scheduled (cron) job

1. **Job** → `GeoClubBot.Infrastructure/InputAdapters/Jobs/<Name>Job.cs`:
   `: IJob`, annotate with `[DisallowConcurrentExecution]` and
   `[ConfiguredCronJob(ConfigKeys.<Name>CronScheduleConfigurationKey)]`. Inject
   `ISender` and delegate to a MediatR command in `Execute(...)`.
2. **Config key** → add the key constant in `Constants/ConfigKeys.cs` and a cron
   expression under that key in `appsettings*.json`.
3. *(Auto-wired — `QuartzModule` scans the Jobs assembly.)* Model on
   `Jobs/DailyMissionReminderJob.cs`.

### 5. Add a config option

1. **Class** → `Configuration/<Feature>Configuration.cs` with a `public const string
   SectionName` and `[Required]`/data-annotation attributes.
2. **Bind it** → add an `AddOptions<...>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`
   block in `Configuration/DependencyInjectionExtensions.cs`.
3. Inject `IOptions<<Feature>Configuration>` where needed.

---

## Scaffolding

`scripts/new-usecase.sh` generates a use case (request record + handler), an optional
validator, and a Discord command module — in the correct folders with the correct
(short-RootNamespace) namespaces.

```bash
# scripts/new-usecase.sh <Feature> <Name> <command|query> [--no-module] [--validator]
scripts/new-usecase.sh Strikes ArchiveStrike command --validator
```

After generating, fill in the request fields, the handler body, and the slash-command
signature, then `dotnet build`. See the script's `--help` for details.

---

## Naming conventions (per layer)

| Layer | Pattern | Example |
|---|---|---|
| Use case request | `<Verb><Noun>Command` / `<Verb><Noun>Query` | `AddStrikeCommand`, `ReadAllStrikesQuery` |
| Use case handler | `<Request-without-suffix>Handler` | `AddStrikeHandler` |
| Validator | `<Request>Validator` | `AddStrikeCommandValidator` |
| Repository port | `I<Aggregate>Repository` | `IStrikesRepository` |
| EF implementation | `Ef<Aggregate>Repository` | `EfStrikesRepository` |
| Caching decorator | `Caching<Aggregate>Repository` | `CachingClubRepository` |
| Discord module | `<Feature>Module` (or `<Action>Modal`) | `ClubStatsModule` |
| Quartz job | `<Feature>Job` | `DailyMissionReminderJob` |
| Config | `<Feature>Configuration` | `SelfRolesConfiguration` |

C# style is enforced by `.editorconfig`: file-scoped namespaces, `using`s **outside**
the namespace (System first), `_camelCase` private fields, Allman braces, `var` when
the type is apparent, 4-space indent.
