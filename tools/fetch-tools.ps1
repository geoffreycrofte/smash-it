<#
.SYNOPSIS
    Downloads the third-party compression tools bundled with Smash It into tools\bin.

.DESCRIPTION
    Every archive is pinned to a version and verified against its SHA-256 before anything is extracted.
    The tools are those of the system the script runs on: Windows x64, macOS on Apple Silicon or Linux x64
    (PowerShell 7, "pwsh", runs there too).

    Windows: jpegtran ships only inside the libjpeg-turbo installer, which is unpacked with 7-Zip
    (install it with: winget install 7zip.7zip). The installer itself is never run.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-tools.ps1

.EXAMPLE
    pwsh tools/fetch-tools.ps1
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

# $IsMacOS and $IsLinux only exist in PowerShell 7; Windows PowerShell 5.1 leaves them empty.
$platform = if ($IsMacOS) { 'osx-arm64' } elseif ($IsLinux) { 'linux-x64' } else { 'win-x64' }

$windowsTools = @(
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
        # Built by this repository's "Build tools" workflow with only what Smash It uses (tools/ffmpeg-configure.sh):
        # one static exe, about a third of a full build. The release carries every source (GPL).
        Name       = 'ffmpeg 9.0.2 (slim)'
        Url        = 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-ffmpeg-9.0.2-slim/ffmpeg-9.0.2-slim-win64.exe'
        Sha256     = '0e25e5d9b7e8ed3e152878807537cf7781911b5632270f9de3bf9fe5151fcd54'
        Files      = @('ffmpeg.exe')
        Executable = $true
        LicenseUrl = @{ 'https://github.com/geoffreycrofte/smash-it/releases/download/tools-ffmpeg-9.0.2-slim/ffmpeg-LICENSE.txt' = 'ffmpeg.txt' }
        License    = @{}
    }
)

# macOS (Apple Silicon) and Linux (x64): the projects' own builds. pngquant, gifsicle, oxvg, jpegtran and ffmpeg have
# none for these systems: the "Build tools" workflow will provide them. Without them Smash It still works, with fewer
# engines.
$unixTools = @(
    @{
        Name     = 'oxipng 10.2.1'
        Platform = 'osx-arm64'
        Url      = 'https://github.com/oxipng/oxipng/releases/download/v10.2.1/oxipng-10.2.1-aarch64-apple-darwin.tar.gz'
        Sha256   = '7039fcfc78e8aa1ed2b57d848057a0296f082e92b3e1807ac65402d10d926764'
        Files    = @('oxipng')
        License  = @{ 'LICENSE' = 'oxipng.txt' }
    },
    @{
        # musl, static: runs on any distribution.
        Name     = 'oxipng 10.2.1'
        Platform = 'linux-x64'
        Url      = 'https://github.com/oxipng/oxipng/releases/download/v10.2.1/oxipng-10.2.1-x86_64-unknown-linux-musl.tar.gz'
        Sha256   = '1813750ef592c5350ca79c88f98b2c0876d05c826dc7156245256d4255c1ad17'
        Files    = @('oxipng')
        License  = @{ 'LICENSE' = 'oxipng.txt' }
    },
    @{
        Name     = 'libwebp 1.6.0'
        Platform = 'osx-arm64'
        Url      = 'https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0-mac-arm64.tar.gz'
        Sha256   = 'bc6bf84cc70f3f8574fba797d1e4a7dea4feebe9fa4be919f202413ea2b3b8f2'
        Files    = @('cwebp')
        License  = @{}
    },
    @{
        Name     = 'libwebp 1.6.0'
        Platform = 'linux-x64'
        Url      = 'https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0-linux-x86-64.tar.gz'
        Sha256   = '1c5ffab71efecefa0e3c23516c3a3a1dccb45cc310ae1095c6f14ae268e38067'
        Files    = @('cwebp')
        License  = @{}
    },
    @{
        Name     = 'libavif 1.4.2'
        Platform = 'osx-arm64'
        Url      = 'https://github.com/AOMediaCodec/libavif/releases/download/v1.4.2/macOS-artifacts.zip'
        Sha256   = '41f9a3db7b7697aa4f9c83d5e07a1b2e00f28f23676d3f27698eef766689a6b6'
        Files    = @('avifenc')
        License  = @{}
    },
    @{
        # Needs glibc 2.34 (Ubuntu 22.04, Debian 12, Fedora 35 and later).
        Name     = 'libavif 1.4.2'
        Platform = 'linux-x64'
        Url      = 'https://github.com/AOMediaCodec/libavif/releases/download/v1.4.2/linux-artifacts.zip'
        Sha256   = 'faf58a670ffbfdc0e3559e6d37592cff277c447dd39453f1cd1d7d7f5a20b8ef'
        Files    = @('avifenc')
        License  = @{}
    },
    @{
        # bin/qpdf finds its library in ../lib: the whole folder is kept, as tools/qpdf/.
        Name       = 'qpdf 12.4.2'
        Platform   = 'osx-arm64'
        Url        = 'https://github.com/qpdf/qpdf/releases/download/v12.4.2/qpdf-12.4.2-bin-macos-arm64.zip'
        Sha256     = '62e46987a30ea167cbc530ccb22690aec3d8c812ed09979a941bcee92e504b79'
        Tree       = 'qpdf'
        Files      = @('bin/qpdf')
        LicenseUrl = @{ 'https://raw.githubusercontent.com/qpdf/qpdf/v12.4.2/LICENSE.txt' = 'qpdf.txt' }
        License    = @{}
    },
    @{
        Name       = 'qpdf 12.4.2'
        Platform   = 'linux-x64'
        Url        = 'https://github.com/qpdf/qpdf/releases/download/v12.4.2/qpdf-12.4.2-bin-linux-x86_64.zip'
        Sha256     = 'db367d897829f22c4198ce1094143c9d467bd6ee7dfabc44ba6f02056b24f8b1'
        Tree       = 'qpdf'
        Files      = @('bin/qpdf')
        LicenseUrl = @{ 'https://raw.githubusercontent.com/qpdf/qpdf/v12.4.2/LICENSE.txt' = 'qpdf.txt' }
        License    = @{}
    }
)

$tools = if ($platform -eq 'win-x64') { $windowsTools } else { @($unixTools | Where-Object { $_.Platform -eq $platform }) }

function Find-7Zip {
    $command = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($dir in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($dir -and (Test-Path (Join-Path $dir '7-Zip\7z.exe'))) { return Join-Path $dir '7-Zip\7z.exe' }
    }
    return $null
}

function Save-Url([string] $Url, [string] $Path) {
    # Hosts sometimes answer slowly or drop the connection: a few attempts, with growing pauses, before giving up.
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-WebRequest $Url -OutFile $Path -UseBasicParsing -TimeoutSec 120
            return
        }
        catch {
            if ($attempt -ge 3) { throw }
            Write-Warning "Téléchargement interrompu ($($_.Exception.Message)), nouvel essai…"
            Start-Sleep -Seconds (10 * $attempt)
        }
    }
}

function Expand-Download([string] $Archive, [string] $Destination) {
    New-Item -ItemType Directory -Force $Destination | Out-Null
    if ($Archive.EndsWith('.tar.gz')) {
        & tar -xzf $Archive -C $Destination
        if ($LASTEXITCODE -ne 0) { throw "tar n'a pas pu extraire $Archive." }
    }
    elseif ($platform -ne 'win-x64') {
        # unzip keeps the executable bits that Expand-Archive drops.
        & unzip -q -o $Archive -d $Destination
        if ($LASTEXITCODE -ne 0) { throw "unzip n'a pas pu extraire $Archive." }
    }
    else {
        Expand-Archive $Archive -DestinationPath $Destination -Force
    }
}

New-Item -ItemType Directory -Force $bin, $licenses, $cache | Out-Null
$sevenZip = if ($platform -eq 'win-x64') { Find-7Zip } else { $null }
$failed = @()

foreach ($tool in $tools) {
    # A "Tree" tool keeps its folder (bin/, lib/) as tools/<Tree>/; the others are single files in tools/.
    $target = if ($tool.Tree) { Join-Path $bin $tool.Tree } else { $bin }
    $present = $tool.Files | Where-Object { -not (Test-Path (Join-Path $target $_)) }
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
        Save-Url $tool.Url $archive
    }

    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash
    if ($hash -ne $tool.Sha256) {
        Remove-Item $archive -Force
        throw "Empreinte SHA-256 inattendue pour $($tool.Name) : $hash"
    }

    if ($tool.Executable) {
        # Downloaded as the program itself: copied under its usual name, license fetched next to it.
        Copy-Item $archive (Join-Path $bin $tool.Files[0]) -Force
        if ($platform -ne 'win-x64') { & chmod +x (Join-Path $bin $tool.Files[0]) }
        foreach ($entry in $tool.LicenseUrl.GetEnumerator()) {
            Save-Url $entry.Key (Join-Path $licenses $entry.Value)
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
        Expand-Download $archive $extracted
    }

    if ($tool.Tree) {
        # The archive's own layout: its top folder when it has one, else everything.
        $top = @(Get-ChildItem $extracted)
        $source = if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $top[0].FullName } else { $extracted }
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Copy-Item $source $target -Recurse
        foreach ($file in $tool.Files) {
            if (-not (Test-Path (Join-Path $target $file))) { throw "$file introuvable dans $($tool.Name)." }
        }
        foreach ($entry in $tool.LicenseUrl.GetEnumerator()) {
            Save-Url $entry.Key (Join-Path $licenses $entry.Value)
        }
        Remove-Item $extracted -Recurse -Force
        Write-Host "✔ $($tool.Name)"
        continue
    }

    foreach ($file in $tool.Files) {
        $found = Get-ChildItem $extracted -Recurse -File -Filter $file | Select-Object -First 1
        if (-not $found) { throw "$file introuvable dans $($tool.Name)." }
        Copy-Item $found.FullName $bin -Force
        if ($platform -ne 'win-x64') { & chmod +x (Join-Path $bin $file) }
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

if ($platform -ne 'win-x64') {
    $failed += 'pngquant', 'gifsicle', 'oxvg', 'jpegtran', 'ffmpeg (pas encore de version pour ce système)'
}
if ($failed) {
    Write-Warning "Outils manquants : $($failed -join ', '). Smash It fonctionnera sans eux."
}
Write-Host "Outils installés dans $bin"
