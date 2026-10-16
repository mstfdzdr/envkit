# winget manifests

Templates for submitting EnvKit to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
They need a published GitHub release, since winget downloads the installer from its URL.

1. Push a `vX.Y.Z` tag; the Release workflow publishes `EnvKit-win-Setup.exe`.
2. Replace `OWNER/REPO`, the version and `InstallerSha256` (`Get-FileHash EnvKit-win-Setup.exe`).
3. Validate locally: `winget validate --manifest packaging/winget/manifest`
   and test the install: `winget install --manifest packaging/winget/manifest`.
4. Submit, e.g. with [wingetcreate](https://github.com/microsoft/winget-create):
   `wingetcreate submit packaging/winget/manifest`.

Later versions: `wingetcreate update EnvKit.EnvKit --version X.Y.Z --urls <installer url> --submit`.

The installer is per-user (`Scope: user`, no administrator rights) and adds `envkit` to the
user PATH from its install hook, so `Commands: envkit` works right after installation.
