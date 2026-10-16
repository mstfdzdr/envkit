<#
.SYNOPSIS
  Writes a neutral (English) .resx and a Turkish .resx from one table, so the two languages
  cannot drift apart.

.DESCRIPTION
  The table is a UTF-8 text file with one entry per line: key<TAB>English<TAB>Turkish.
  Lines starting with # are comments. "\n" in a value becomes a line break.

.EXAMPLE
  ./build/New-Resx.ps1 -Table src/EnvKit.App/Resources/Strings.txt -Output src/EnvKit.App/Resources/Strings
#>
param(
    [Parameter(Mandatory)] [string] $Table,
    [Parameter(Mandatory)] [string] $Output
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$lines = [IO.File]::ReadAllLines((Resolve-Path $Table), $utf8)

$header = @'
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'@

function Escape([string] $text) {
    [Security.SecurityElement]::Escape($text.Replace('\n', "`n"))
}

$english = New-Object Text.StringBuilder $header
$turkish = New-Object Text.StringBuilder $header
$seen = @{}
$number = 0
foreach ($line in $lines) {
    $number++
    if ($line.Trim().Length -eq 0 -or $line.StartsWith('#')) { continue }
    $parts = $line.Split("`t")
    if ($parts.Count -ne 3) { throw "${Table}:${number}: expected key<TAB>English<TAB>Turkish." }
    $key = $parts[0].Trim()
    if ($seen.ContainsKey($key)) { throw "${Table}:${number}: duplicate key $key." }
    $seen[$key] = $true
    [void]$english.AppendLine("  <data name=""$key"" xml:space=""preserve""><value>$(Escape $parts[1])</value></data>")
    [void]$turkish.AppendLine("  <data name=""$key"" xml:space=""preserve""><value>$(Escape $parts[2])</value></data>")
}

[void]$english.AppendLine('</root>')
[void]$turkish.AppendLine('</root>')
[IO.File]::WriteAllText("$Output.resx", $english.ToString(), $utf8)
[IO.File]::WriteAllText("$Output.tr.resx", $turkish.ToString(), $utf8)
Write-Host "$($seen.Count) strings -> $Output.resx, $Output.tr.resx"
