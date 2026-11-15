<#
.SYNOPSIS
    Downloads the third-party compression tools bundled with Smash It into tools\bin.

.DESCRIPTION
    Every archive is pinned to a version and verified against its SHA-256 before anything is extracted.
    jpegtran ships only inside the libjpeg-turbo installer, which is unpacked with 7-Zip
    (install it with: winget install 7zip.7zip). The installer itself is never run.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-tools.ps1
#>
[CmdletBinding()]
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Windows PowerShell 5.1 writes in the legacy console code page; force UTF-8 so accents and symbols survive.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
$licenses = Join-Path $bin 'licenses'
$cache = Join-Path ([IO.Path]::GetTempPath()) 'SmashIt.tools'

$tools = @(
    @{
        Name    = 'oxipng 10.2.1'
        Url     = 'https://github.com/oxipng/oxipng/releases/download/v10.2.1/oxipng-10.2.1-x86_64-pc-windows-msvc.zip'
        Sha256  = '7e940f83ee46874b73f53031f96a15834cb70b220af27391fb06fe7b4dd798e1'
        Files   = @('oxipng.exe')
        License = @{ 'LICENSE.txt' = 'oxipng.txt' }
    },
    @{
        Name    = 'libwebp 1.6.0'
        Url     = 'https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0-windows-x64.zip'
        Sha256  = '48886f506b21f62e4661f0f4cbfca19800897c385128e8902542d29a950c93f1'
        Files   = @('cwebp.exe')
        License = @{}
    },
    @{
        Name    = 'libavif 1.4.2'
        Url     = 'https://github.com/AOMediaCodec/libavif/releases/download/v1.4.2/windows-artifacts.zip'
        Sha256  = 'cb2d9fea43dcbab1d0707e3b37eb7b08070ad2fb60a2c188c39ec12382c0484a'
        Files   = @('avifenc.exe')
        License = @{}
    },
    @{
        Name    = 'libjpeg-turbo 3.2.1'
        Url     = 'https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.2.1/libjpeg-turbo-3.2.1-vc-x64.exe'
        Sha256  = '1f05bc3ed1585e6b12a8b0c24c8c5f07b1f6d90d01b4b709b331bedc9d6e5838'
        Files   = @('jpegtran.exe')
        License = @{ 'LICENSE.md' = 'libjpeg-turbo.md' }
        Dlls    = @('jpeg62.dll')
        Needs7z = $true
    }
)

function Find-7Zip {
    $command = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($dir in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($dir -and (Test-Path (Join-Path $dir '7-Zip\7z.exe'))) { return Join-Path $dir '7-Zip\7z.exe' }
    }
    return $null
}

New-Item -ItemType Directory -Force $bin, $licenses, $cache | Out-Null
$sevenZip = Find-7Zip
$failed = @()

foreach ($tool in $tools) {
    $present = $tool.Files | Where-Object { -not (Test-Path (Join-Path $bin $_)) }
    if (-not $Force -and -not $present) {
        Write-Host "✔ $($tool.Name) déjà présent"
        continue
    }

    if ($tool.Needs7z -and -not $sevenZip) {
        Write-Warning "$($tool.Name) ignoré : 7-Zip est nécessaire pour l'extraire (winget install 7zip.7zip)."
        $failed += $tool.Name
        continue
    }

    $archive = Join-Path $cache ([IO.Path]::GetFileName($tool.Url))
    if (-not (Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $tool.Sha256) {
        Write-Host "↓ $($tool.Name)"
        Invoke-WebRequest $tool.Url -OutFile $archive -UseBasicParsing
    }

    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash
    if ($hash -ne $tool.Sha256) {
        Remove-Item $archive -Force
        throw "Empreinte SHA-256 inattendue pour $($tool.Name) : $hash"
    }

    $extracted = Join-Path $cache ([IO.Path]::GetFileNameWithoutExtension($archive))
    if (Test-Path $extracted) { Remove-Item $extracted -Recurse -Force }
    if ($tool.Needs7z) {
        & $sevenZip x $archive "-o$extracted" -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7-Zip n'a pas pu extraire $($tool.Name)." }
    }
    else {
        Expand-Archive $archive -DestinationPath $extracted -Force
    }

    foreach ($file in $tool.Files) {
        $found = Get-ChildItem $extracted -Recurse -Filter $file | Select-Object -First 1
        if (-not $found) { throw "$file introuvable dans $($tool.Name)." }
        Copy-Item $found.FullName $bin -Force
        # DLLs the tool depends on sit next to it (jpegtran needs jpeg62.dll).
        foreach ($dll in @($tool.Dlls | Where-Object { $_ })) {
            $dllPath = Join-Path $found.DirectoryName $dll
            if (-not (Test-Path $dllPath)) { throw "$dll introuvable dans $($tool.Name)." }
            Copy-Item $dllPath $bin -Force
        }
    }
    foreach ($entry in $tool.License.GetEnumerator()) {
        $found = Get-ChildItem $extracted -Recurse -Filter $entry.Key | Select-Object -First 1
        if ($found) { Copy-Item $found.FullName (Join-Path $licenses $entry.Value) -Force }
    }

    Remove-Item $extracted -Recurse -Force
    Write-Host "✔ $($tool.Name)"
}

if ($failed) {
    Write-Warning "Outils manquants : $($failed -join ', '). Smash It fonctionnera sans eux."
}
Write-Host "Outils installés dans $bin"
