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

### Local pre-commit hook

Run `./scripts/install-git-hooks.sh` once after cloning to point `core.hooksPath` at
`.githooks/`. The `pre-commit` hook verifies formatting (`dotnet format --verify-no-changes`)
and runs the fast unit tests, so CI failures are caught before you push. Bypass a single
commit with `git commit --no-verify`.

## Branching model

| Branch | Purpose | What runs |
|---|---|---|
| `feature/*` | In-development work | Build + full tests on push (`ci.yml`) |
| `dev` | Reviewed features, not yet released | Full tests + publishes `ghcr.io/efibs/geo-club-bot:dev` (`dev-image.yml`) |
| `master` | Releases only | A pushed SemVer tag (e.g. `0.13.0`) tests, publishes the versioned image + `:latest`, and creates a GitHub Release (`release.yml`) |

Flow: branch off `dev` → open a PR into `dev` (the full suite must pass before merge) →
when a set of features is ready to ship, merge `dev` → `master` and push a version tag.

### CI/CD workflows (`.github/workflows/`)

- **`tests.yml`** — reusable suite (fast unit job + Docker-backed integration job, each with
  a coverage summary). The other workflows call it so "run all tests" lives in one place.
- **`ci.yml`** — runs `tests.yml` on every PR into `dev`/`master` and on feature-branch
  pushes. **Make `Unit tests (fast)` and `Integration tests (Docker)` required status checks**
  on `dev` and `master` in branch protection so PRs can't merge until tests pass.
- **`dev-image.yml`** — on push to `dev`: tests, then publishes the `:dev` staging image.
- **`release.yml`** — on a SemVer tag: tests, then publishes the versioned image, `:latest`,
  and a GitHub Release with auto-generated notes.

Images authenticate to GHCR with the built-in `GITHUB_TOKEN` (`packages: write`) — no
`CR_PAT` secret is required in CI. **Dependabot** (`.github/dependabot.yml`) keeps NuGet
packages, the workflow actions, and the Docker base image up to date.
