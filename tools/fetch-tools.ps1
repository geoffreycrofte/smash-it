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
    },
    @{
        # pngquant.org only publishes an unversioned zip (2.17.0, unchanged since December 2023), and sometimes too
        # slowly for CI: this repository's "Build tools" workflow mirrors it unchanged, with its source. The
        # SHA-256 is that of pngquant.org's zip.
        Name    = 'pngquant 2.17.0'
        Url     = 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-pngquant-2.17.0/pngquant-2.17.0-windows.zip'
        Sha256  = 'bd0257aeeccfe446a4cd764927e26f8af6051796f28abed104307284107b120d'
        Files   = @('pngquant.exe')
        License = @{ 'COPYRIGHT' = 'pngquant.txt' }
    },
    @{
        # Windows builds by Jernej Simončič, linked from the official site (lcdf.org/gifsicle). No checksum is
        # published there: this SHA-256 was taken from the first download.
        Name    = 'gifsicle 1.97'
        Url     = 'https://eternallybored.org/misc/gifsicle/releases/gifsicle-1.97-win64.zip'
        Sha256  = 'bb3ad4e1521a3953152f406eaafabda153fb5986938405860a7e7f12a3d13227'
        Files   = @('gifsicle.exe')
        License = @{ 'COPYING' = 'gifsicle.txt' }
    },
    @{
        # oxvg's own release only has an ARM64 Windows exe: this x64 one is compiled from its v0.0.9 tag by this
        # repository's "Build tools" workflow (.github/workflows/tools.yml). A single exe, no archive.
        Name       = 'oxvg 0.0.9'
        Url        = 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-oxvg-0.0.9/oxvg-0.0.9-x86_64-pc-windows-msvc.exe'
        Sha256     = 'fb45c074180c0fb17626e56335e124280307f123fc5c981be9b78e4e3a0c4aae'
        Files      = @('oxvg.exe')
        Executable = $true
        LicenseUrl = @{ 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-oxvg-0.0.9/oxvg-LICENSE.txt' = 'oxvg.txt' }
        License    = @{}
    },
    @{
        Name    = 'qpdf 12.4.2'
        Url     = 'https://github.com/qpdf/qpdf/releases/download/v12.4.2/qpdf-12.4.2-msvc64.zip'
        Sha256  = 'db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d'
        Files   = @('qpdf.exe')
        # qpdf's library and the Visual C++ runtime it ships with (app-local redistributable).
        Dlls    = @('qpdf*.dll', 'msvcp140*.dll', 'vcruntime140*.dll', 'concrt140.dll')
        License = @{ 'license.rst.txt' = 'qpdf.txt' }
    },
    @{
        # BtbN's static GPL build (x264, x265, SVT-AV1…), mirrored unchanged by this repository's "Build tools"
        # workflow with its sources, because BtbN deletes dated builds after a while. Encodes AV1 videos.
        Name       = 'ffmpeg 9.0.2'
        Url        = 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-ffmpeg-9.0.2/ffmpeg-9.0.2-win64-gpl.exe'
        Sha256     = 'bd2a2260fe651c7b0975b86e58c4519f9884a2b6756c909c8a5becf4de074342'
        Files      = @('ffmpeg.exe')
        Executable = $true
        LicenseUrl = @{ 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-ffmpeg-9.0.2/ffmpeg-LICENSE.txt' = 'ffmpeg.txt' }
        License    = @{}
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

    if ($tool.Executable) {
        # Downloaded as the program itself: copied under its usual name, license fetched next to it.
        Copy-Item $archive (Join-Path $bin $tool.Files[0]) -Force
        foreach ($entry in $tool.LicenseUrl.GetEnumerator()) {
            Invoke-WebRequest $entry.Key -OutFile (Join-Path $licenses $entry.Value) -UseBasicParsing
        }
        Write-Host "✔ $($tool.Name)"
        continue
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
        # DLLs the tool depends on sit next to it (jpegtran needs jpeg62.dll, qpdf its own DLL + VC++ runtime).
        foreach ($pattern in @($tool.Dlls | Where-Object { $_ })) {
            $dlls = Get-ChildItem $found.DirectoryName -Filter $pattern
            if (-not $dlls) { throw "$pattern introuvable dans $($tool.Name)." }
            $dlls | Copy-Item -Destination $bin -Force
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
