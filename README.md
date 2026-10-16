# EnvKit

[![CI](https://github.com/mstfdzdr/envkit/actions/workflows/ci.yml/badge.svg)](https://github.com/mstfdzdr/envkit/actions/workflows/ci.yml)

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
- **Credential Manager, usable:** see Windows credentials grouped by the tool that owns them
  (git, GitHub Desktop, Visual Studio, VS Code, Docker, Remote Desktop, …), search them, edit
  them, delete many at once, and export an inventory without any secrets. Deletes and edits are
  snapshotted too, so a bulk cleanup can be undone. (Domain credentials are the exception:
  Windows never reveals their secret, so deleting one is permanent, and EnvKit says so first.)
- **Profiles:** named sets of changes such as "java21" (set `JAVA_HOME`, put `%JAVA_HOME%\bin`
  first in PATH). Apply one to your environment (reviewed and undoable like any change), or use
  it for a single command with `envkit run`.
- **A vault for secrets:** API keys and tokens live in a DPAPI-encrypted vault instead of your
  environment. Profiles reference them as `${vault:NAME}`, and `envkit run` injects them into the
  one process that needs them; they are never written to the persistent environment.
- **.env files and .NET user secrets:** find them across a folder tree, see which `.env` files
  are committed to git or missing from `.gitignore`, and edit keys without disturbing the rest of
  the file: comments, quoting, `export`, line endings and byte order marks stay exactly as they
  were. Move a secret out of a `.env` into the vault, leaving `${vault:NAME}` behind, and
  `envkit run --env-file .env` fills it back in for the process.

## Install

Download `EnvKit-win-Setup.exe` from the [latest release](https://github.com/mstfdzdr/envkit/releases/latest).
It installs per user (no administrator rights), adds `envkit` to your user PATH and updates
itself from GitHub Releases. A winget package is on the way.

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

envkit cred list [--filter git:*] [--group Git]      credentials grouped by tool; secrets never shown
envkit cred show <target> [--reveal]                 details; the secret only with --reveal
envkit cred set <target> [--user u] [--password-stdin]   prompts for the password (never an argument)
envkit cred delete <target>... | --filter f | --group g  bulk delete, undoable
envkit cred export <file.json>                       inventory without secrets

envkit profile new <name> [--set N=V] [--unset N] [--remove-path D] [--prepend-path D] [--append-path D]
envkit profile list | show <name> | apply <name> | delete <name> | import <file> | export <name> <file>
envkit vault list | set <name> [--value-stdin] | get <name> | delete <name>
envkit run [-p profile]... [-e .env]... [-s NAME[=SECRET]]... -- <command> [args]

envkit dotenv scan [folder]                          .env files and user-secrets projects, with git warnings
envkit dotenv list | get <key> [-f .env]             values that look secret are masked
envkit dotenv set <key> [value] | unset <key> | rename <key> <new>
envkit dotenv link <key> <vault-name> [--move]       replace the value with ${vault:name}
envkit user-secrets list | set <key> [value] | unset <key> [-p project]
```

For example, keep a token out of your environment and still give it to one tool:

```powershell
envkit vault set github                     # prompts for the value
envkit run -s GITHUB_TOKEN=github -- gh repo list
```

Or switch JDKs per command without touching your environment:

```powershell
envkit profile new java21 --set "JAVA_HOME=C:\jdk-21" --prepend-path "%JAVA_HOME%\bin"
envkit run -p java21 -- mvn package
```

Profiles are plain JSON files (`envkit profile export`), so they can be shared or kept in a
repository; they hold `${vault:...}` references, never secrets.

Or get a live key out of a project's `.env` without breaking the app:

```powershell
envkit dotenv link STRIPE_SECRET stripe --move     # value goes to the vault, the file keeps ${vault:stripe}
envkit run -e .env -- npm start                    # the process still sees the real value
```

Like every other change, `.env` and user-secrets edits are previewed, refused if the file
changed in the meantime, written atomically, and can be undone from History.

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
| `EnvKit.Core` | Platform-neutral engine: PATH analysis, expansion, credential model, change plans, snapshots |
| `EnvKit.Windows` | Registry and Credential Manager stores, `WM_SETTINGCHANGE`, DPAPI, UAC helper |
| `EnvKit.Cli` | `envkit` |
| `EnvKit.App` | WPF desktop app |

Design notes and decisions live in [docs/DESIGN.md](docs/DESIGN.md).

## Data

Snapshots are stored encrypted in `%LOCALAPPDATA%\EnvKit.Data`, separate from the install
folder, so they survive an uninstall. Delete that folder to remove them. A snapshot holds all
environment variables and, for credential changes, the previous state (including the secret)
of only the credentials and files that change touched; the 100 most recent snapshots are kept. Profiles
are JSON files in `profiles\` and the vault is `vault.dat` (DPAPI-encrypted) in the same folder.
Vault secrets are not snapshotted, so deleting one cannot be undone.

## License

[MIT](LICENSE)
