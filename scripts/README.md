# `scripts/`

Developer helper scripts for GeoClubBot.

| Script | What it does |
|---|---|
| `new-usecase.sh` | Scaffolds a use case (request + handler) and optional validator / Discord command module in the correct folders with the right namespaces. Run `scripts/new-usecase.sh --help` for arguments. |
| `new-usecase-gui.sh` | A [`zenity`](https://help.gnome.org/users/zenity/stable/) form front-end for `new-usecase.sh`. Pops a single dialog with labeled fields, then calls the scaffolder. Meant to be wired into Rider as an External Tool. |
| `install-git-hooks.sh` | One-time setup: points `core.hooksPath` at `.githooks/` so the `pre-commit` hook (formatting check + fast unit tests) runs locally. Bypass a commit with `git commit --no-verify`. |

## Using `new-usecase.sh` from the command line

```bash
scripts/new-usecase.sh Strikes ArchiveStrike command --validator
scripts/new-usecase.sh Club GetClubBadges query --no-module
```

The generated files compile as-is; fill in the TODOs, then run `dotnet build GeoClubBot.sln`.

## Scaffolding a use case from Rider's UI

`new-usecase-gui.sh` lets you trigger the scaffolder from a form inside Rider instead
of the terminal.

### Prerequisites

- **Linux** with `zenity` installed (it ships with GNOME; on Debian/Ubuntu/Mint:
  `sudo apt install zenity`). The script exits with a clear message if `zenity` is
  missing — fall back to `new-usecase.sh` directly.

### One-time setup in Rider

1. Open **Settings → Tools → External Tools** and click **➕**.
2. Fill in:
   | Field | Value |
   |---|---|
   | **Name** | `New Use Case` |
   | **Program** | `$ProjectFileDir$/scripts/new-usecase-gui.sh` |
   | **Arguments** | *(leave empty — the form collects them)* |
   | **Working directory** | `$ProjectFileDir$` |
3. (Optional) Keep **Open console for output** checked to also see the `WROTE …`
   lines in Rider's console.
4. Click **OK**.

> The External Tool definition is stored per-IDE (under
> `~/.config/JetBrains/<Rider>/tools/`), not in the repo, so each developer adds it once.

### Running it

- **Tools → External Tools → New Use Case**, or
- assign a shortcut / toolbar button via **Settings → Keymap** (search "New Use Case").

A single window appears with **Feature** and **Name** text fields, a **Kind** dropdown
(`command` / `query`), and dropdowns for the validator and module toggles. Click **OK**
and the script scaffolds the files, then shows a summary dialog.