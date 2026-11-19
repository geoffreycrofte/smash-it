<#
.SYNOPSIS
    Development shortcuts for Smash It. Run through dev.cmd at the repository root: `.\dev run`.

.EXAMPLE
    .\dev run                      # the app
    .\dev run C:\Photos            # the app, compressing a folder right away
    .\dev cli -q 75 C:\Photos      # the command line
    .\dev test
    .\dev release -SkipInstaller
#>

# No param() block on purpose: PowerShell would then claim options such as "-e" (ambiguous with -ErrorAction)
# instead of passing them on to the app or the command line.
$Command = if ($args.Count -gt 0) { [string]$args[0] } else { 'help' }
[string[]] $Arguments = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts'

function Invoke-Dotnet([string[]] $DotnetArguments) {
    & dotnet @DotnetArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Invoke-Script([string] $Name, [string[]] $ScriptArguments) {
    # A child PowerShell parses the options as typed: "-SkipInstaller" becomes a switch, not a string.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $Name) @ScriptArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

switch ($Command) {
    'run' {
        Invoke-Dotnet (@('run', '--project', (Join-Path $repo 'src\SmashIt.App'), '--') + $Arguments)
    }
    'cli' {
        Invoke-Dotnet (@('run', '--project', (Join-Path $repo 'src\SmashIt.Cli'), '--') + $Arguments)
    }
    'build' {
        Invoke-Dotnet (@('build', (Join-Path $repo 'SmashIt.slnx')) + $Arguments)
    }
    'test' {
        Invoke-Dotnet (@('test', '--solution', (Join-Path $repo 'SmashIt.slnx')) + $Arguments)
    }
    'tools' {
        Invoke-Script 'fetch-tools.ps1' $Arguments
    }
    'release' {
        Invoke-Script 'publish.ps1' $Arguments
    }
    'setup' {
        # Builds the installer and starts it, as someone downloading it would.
        Invoke-Script 'publish.ps1' @()
        Start-Process (Join-Path $artifacts 'SmashIt-Setup-x64.exe')
    }
    'clean' {
        # Build folders only: tools\bin (the bundled tools) stays.
        $folders = @($artifacts) + (Get-ChildItem $repo -Directory -Recurse -Include bin, obj |
            Where-Object { $_.FullName -notlike (Join-Path $repo 'tools\*') } | ForEach-Object FullName)
        foreach ($folder in $folders | Where-Object { Test-Path $_ }) {
            try { Remove-Item $folder -Recurse -Force; Write-Host "✔ $folder" }
            catch { Write-Warning "$folder est verrouillé (Smash It encore ouvert ?) : $($_.Exception.Message)" }
        }
    }
    default {
        Write-Host @'
Raccourcis de développement Smash It : .\dev <commande> [arguments]

  run [fichiers…]     Lance l'app (les fichiers ou dossiers donnés sont compressés tout de suite)
  cli [options…]      Lance la ligne de commande, ex. : .\dev cli -q 75 C:\Photos
  build               Compile toute la solution
  test                Lance les tests
  tools               Télécharge les outils embarqués (oxipng, pngquant…) dans tools\bin
  release             Construit l'installeur et le zip dans artifacts\ (-SkipInstaller : zip seul)
  setup               Construit l'installeur et le lance
  clean               Supprime les dossiers de build (bin, obj, artifacts)
'@
    }
}
