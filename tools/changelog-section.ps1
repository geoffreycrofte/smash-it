<#
.SYNOPSIS
    Prints the section of CHANGELOG.md for one version (the text under "## vX.Y.Z"), or nothing.

.DESCRIPTION
    Same rules as the app (SmashIt.Core.Updates.Changelog): a level-2 heading per version, "v" and brackets
    optional, anything after the number (a date) ignored.

.EXAMPLE
    .\tools\changelog-section.ps1 -Version 0.2.0
#>
param([Parameter(Mandatory)] [string] $Version)

$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'CHANGELOG.md'
if (-not (Test-Path $path)) { return }

$wanted = $Version.TrimStart('v')
$inside = $false
$lines = foreach ($line in Get-Content $path -Encoding UTF8) {
    if ($line -match '^#{1,2} ') {
        if ($inside) { break }
        $inside = $line -match ('^##\s+\[?v?' + [regex]::Escape($wanted) + '\]?(\s|$)')
        continue
    }
    if ($inside) { $line }
}
# Lines wrapped in the file are joined back (GitHub shows each line break of a release text), like the app does.
$joined = [Collections.Generic.List[string]]::new()
$previousBlank = $true
foreach ($line in $lines) {
    $line = $line.TrimEnd()
    if (-not $line.Trim()) { $joined.Add(''); $previousBlank = $true; continue }
    $startsBlock = $line -match '^\s*(#{1,6}\s|[-*+]\s|\d+\.\s|>|---|\*\*\*|<!--)'
    if (-not $previousBlank -and $joined.Count -gt 0 -and -not $startsBlock -and -not $joined[$joined.Count - 1].TrimStart().StartsWith('#')) {
        $joined[$joined.Count - 1] += ' ' + $line.Trim()
    }
    else { $joined.Add($line) }
    $previousBlank = $false
}
($joined -join "`n").Trim()
