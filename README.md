# EnvKit

Manage Windows environment variables and PATH without the guesswork: see what is broken,
preview every change as a diff, and undo anything.

EnvKit is a desktop app and a CLI (`envkit`) that share one engine.

## Why

The built-in editor shows PATH as a small list and nothing else. It does not tell you that an
entry points nowhere, appears three times, or never takes effect; it keeps no history; and it
can silently break `%VARIABLE%` references. EnvKit does those things:

- **PATH health check:** missing directories, duplicates (after expansion, case-insensitive),
  empty and quoted entries, relative paths, unresolved `%VARIABLES%`, and references that never
  expand because PATH is stored as `REG_SZ`.
- **Shadowing:** `envkit which python` lists every match in search order, so you can see
  which one runs and which ones (including the Microsoft Store alias) are shadowed.
- **Preview, then apply:** every change is shown as a diff first; PATH changes are diffed per entry.
- **Undo:** a snapshot of all variables is taken before every change. Restore any of them from
  History or with `envkit snapshot restore`.
- **Safe writes:** values keep their registry type (`REG_EXPAND_SZ` stays expandable), changes
  are refused if something else edited the variable since the preview, and a failed write
  reverts the rest.
- **Least privilege:** the app runs as a normal user and asks for administrator rights (one UAC
  prompt) only for machine-scope changes.
- **Secrets stay hidden:** variables that look like tokens or passwords are masked until you ask,
  and snapshots are encrypted with DPAPI.

## Install

Download `EnvKit-win-Setup.exe` from the latest release. It installs per user (no
administrator rights) and adds `envkit` to your user PATH. A winget package is on the way.

## CLI

```text
envkit path list [--scope user|machine] [--check]   list PATH, problems marked (--check exits 1 if any)
envkit path add <dir> [--scope ...] [--prepend]      add a directory (may use %VARIABLES%)
envkit path remove <dir> [--scope ...]               remove it, however it is spelled
envkit path fix [--remove-missing] [--unquote]       remove empty and duplicate entries
envkit which <command>                               which file runs, and what it shadows
envkit var list [--scope ...] [--show-secrets]
envkit var get <name> [--scope ...] [--expand]
envkit var set <name> <value> [--scope ...] [--expand | --literal]
envkit var unset <name> [--scope ...]
envkit snapshot list | diff <id> | restore <id>
```

Writing commands show the diff and ask before applying. Use `--dry-run` to only preview and
`--yes` to skip the question (required when input is redirected, e.g. in scripts). Scope
defaults to `user`.

Changes reach programs started afterwards. Terminals that are already open keep their old
environment, which is how Windows works.

## Build from source

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build EnvKit.slnx
dotnet test EnvKit.slnx
dotnet run --project src/EnvKit.App          # desktop app
dotnet run --project src/EnvKit.Cli -- path list --check
./build/pack.ps1 -Version 0.1.0               # installer into artifacts/release
```

The tests never write to your real environment: registry tests use throwaway keys under
`HKCU\Software\EnvKit.Tests-*`.

## Layout

| Project | Contents |
|---|---|
| `EnvKit.Core` | Platform-neutral engine: PATH analysis, expansion, change plans, snapshots |
| `EnvKit.Windows` | Registry store, `WM_SETTINGCHANGE`, DPAPI, UAC helper |
| `EnvKit.Cli` | `envkit` |
| `EnvKit.App` | WPF desktop app |

Design notes and the roadmap live in [docs/DESIGN.md](docs/DESIGN.md). Next up: Windows
Credential Manager (v0.2), profiles and a secret vault (v0.3), and `.env` / user-secrets
editing (v0.4).

## Data

Snapshots are stored encrypted in `%LOCALAPPDATA%\EnvKit.Data`, separate from the install
folder, so they survive an uninstall. Delete that folder to remove them.

## License

[MIT](LICENSE)
