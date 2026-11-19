<#
.SYNOPSIS
    Builds a Smash It release: a portable zip and a Windows installer.

.DESCRIPTION
    Publishes the app and the command-line tool (self-contained, sharing one runtime) into one folder,
    adds the bundled tools, the licenses and a `smashit` command shim, then writes into artifacts\:

      SmashIt-win-x64.zip      portable build, runs from any folder
      SmashIt-Setup-x64.exe    installer (per-user by default, no admin rights needed)
      SHA256SUMS.txt

    File names carry no version, so https://github.com/geoffreycrofte/smash-it/releases/latest/download/<name>
    always points to the latest release.

    The installer needs Inno Setup 6 (winget install JRSoftware.InnoSetup).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Version 0.2.0 -SkipInstaller
#>
[CmdletBinding()]
param(
    # Defaults to the <Version> in Directory.Build.props.
    [string] $Version,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts'
# The bundled tools are x64 builds; on Windows on ARM the whole app runs under x64 emulation.
$runtime = 'win-x64'

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Version invalide : '$Version' (attendu : 1.2.3 ou 1.2.3-beta.1)." }
Write-Host "Smash It $Version ($runtime)"

# 1. Bundled tools: a release without them would silently fall back to WIC only.
& (Join-Path $PSScriptRoot 'fetch-tools.ps1')
$toolsBin = Join-Path $PSScriptRoot 'bin'
$required = 'oxipng.exe', 'pngquant.exe', 'gifsicle.exe', 'jpegtran.exe', 'cwebp.exe', 'avifenc.exe', 'qpdf.exe'
$missing = $required | Where-Object { -not (Test-Path (Join-Path $toolsBin $_)) }
if ($missing) { throw "Outils manquants pour une release : $($missing -join ', ')." }

# 2. Publish the app and the CLI into one folder. A separate build folder keeps a running dev build
#    (which locks bin\) from breaking the release.
$stage = Join-Path $artifacts 'publish\SmashIt'
$build = Join-Path $artifacts 'build\'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

$common = @('-c', 'Release', '-r', $runtime, '--self-contained', '-o', $stage,
    "-p:Version=$Version", "-p:BaseOutputPath=$build", '-nologo', '-v', 'quiet')
foreach ($project in 'src\SmashIt.App', 'src\SmashIt.Cli') {
    Write-Host "→ dotnet publish $project"
    & dotnet publish (Join-Path $repo $project) @common -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué pour $project." }
}

# 3. Release extras: no debug symbols, licenses, and the `smashit` command.
Get-ChildItem $stage -Recurse -Filter *.pdb | Remove-Item -Force
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $stage
$cmd = New-Item -ItemType Directory -Force (Join-Path $stage 'cmd')
Set-Content (Join-Path $cmd 'smashit.cmd') -Encoding ASCII -Value @(
    '@echo off',
    'rem Smash It command line. Add this folder to PATH to run "smashit" from anywhere.',
    '"%~dp0..\SmashIt.Cli.exe" %*'
)

foreach ($exe in 'SmashIt.exe', 'SmashIt.Cli.exe', 'SmashIt.pri') {
    if (-not (Test-Path (Join-Path $stage $exe))) { throw "$exe absent du dossier publié." }
}

# 4. Portable zip (one top-level "Smash It" folder).
$zip = Join-Path $artifacts 'SmashIt-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host '→ zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipRoot = Join-Path $artifacts 'publish\zip'
if (Test-Path $zipRoot) { Remove-Item $zipRoot -Recurse -Force }
New-Item -ItemType Directory -Force $zipRoot | Out-Null
Move-Item $stage (Join-Path $zipRoot 'Smash It')
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($zipRoot, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    Move-Item (Join-Path $zipRoot 'Smash It') $stage
}
$outputs = @($zip)

# 5. Installer.
if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 introuvable (winget install JRSoftware.InnoSetup), ou relancez avec -SkipInstaller." }

    Write-Host '→ installeur'
    $numeric = ($Version -split '-')[0]
    & $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$numeric.0" "/DSourceDir=$stage" "/DOutputDir=$artifacts" `
        "/DRepoDir=$repo" (Join-Path $repo 'installer\SmashIt.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup a échoué." }
    $outputs += Join-Path $artifacts 'SmashIt-Setup-x64.exe'
}

# 6. Checksums, so people can verify what they downloaded.
$sums = Join-Path $artifacts 'SHA256SUMS.txt'
$outputs | ForEach-Object { "$((Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path $_ -Leaf)" } |
    Set-Content $sums -Encoding ASCII

Write-Host ''
foreach ($file in $outputs + $sums) {
    Write-Host ("✔ {0} ({1:N1} Mo)" -f (Split-Path $file -Leaf), ((Get-Item $file).Length / 1MB))
}
