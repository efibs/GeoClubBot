# Contributing to GeoClubBot

New here? Start with the **[Developer Guide](Documentation/DeveloperGuide.md)** — it
explains how the solution is laid out and gives step-by-step recipes for the common
tasks (adding a slash command, a use case, a repository, a job, a config option).

Quick links:

- **Architecture & build/test commands** → [`CLAUDE.md`](CLAUDE.md)
- **Where does X go? / how to add X** → [`Documentation/DeveloperGuide.md`](Documentation/DeveloperGuide.md)
- **Error handling (`Result<T>`)** → [`Documentation/ResultConventions.md`](Documentation/ResultConventions.md)
- **Scaffold a new use case + command** → `scripts/new-usecase.sh` (see the Developer Guide)

## Before you push

```bash
dotnet build GeoClubBot.sln
dotnet test GeoClubBot.Tests/GeoClubBot.Tests.csproj --filter "Category!=Integration"
```

Integration tests (`Category=Integration`) need a running Docker daemon (Testcontainers
spins up PostgreSQL). Run the full `dotnet test` to include them.
