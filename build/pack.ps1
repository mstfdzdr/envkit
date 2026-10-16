<#
.SYNOPSIS
  Publishes EnvKit and builds the Velopack installer.

.DESCRIPTION
  Layout inside the install folder (%LOCALAPPDATA%\EnvKit\current):
    EnvKit.exe + runtime   the desktop app (self-contained)
    bin\envkit.exe         the CLI as a single file, alone in its folder so that only it
                           lands on PATH (the install hook adds bin to the user PATH)

.EXAMPLE
  ./build/pack.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$release = Join-Path $root 'artifacts\release'

function Assert-Success([string] $Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

foreach ($dir in $publish, $release) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}

dotnet publish (Join-Path $root 'src\EnvKit.App\EnvKit.App.csproj') `
    -c $Configuration -r $Runtime --self-contained -o $publish "-p:Version=$Version"
Assert-Success 'Publishing the app'

dotnet publish (Join-Path $root 'src\EnvKit.Cli\EnvKit.Cli.csproj') `
    -c $Configuration -r $Runtime --self-contained -o (Join-Path $publish 'bin') "-p:Version=$Version" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
Assert-Success 'Publishing the CLI'

dotnet tool restore
Assert-Success 'Restoring tools'

dotnet vpk pack `
    --packId EnvKit `
    --packVersion $Version `
    --packTitle EnvKit `
    --packAuthors 'EnvKit contributors' `
    --packDir $publish `
    --mainExe EnvKit.exe `
    --icon (Join-Path $root 'src\EnvKit.App\Assets\EnvKit.ico') `
    --runtime $Runtime `
    --outputDir $release
Assert-Success 'Packing'

Write-Host "Installer and update packages: $release"
