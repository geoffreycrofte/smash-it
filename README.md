# Smash It

A native Windows app to make images, videos and PDFs smaller — in the spirit of ImageOptim on macOS.

Drop files or folders on the window: Smash It tries every engine that can handle them, keeps the smallest
result, and only touches your file when it actually got smaller. Originals go to the Recycle Bin by default.

> Status: early development (v0.1). Image compression works today; see the roadmap below for what's coming.

## Features

| | Today | Planned |
|---|---|---|
| **Images** | JPEG, PNG, TIFF, BMP, JPEG XR, HEIC via the Windows Imaging Component; lossless JPEG (jpegtran) and PNG (oxipng) optimization; WebP (cwebp) and AVIF (avifenc) encoding | gifsicle, svgo, pngquant |
| **Videos** | — | H.264/HEVC through Media Foundation (hardware encoders), ffmpeg for fine tuning |
| **PDF** | Images recompressed in place (qpdf + jpegtran), object streams, Flate level 9; text, fonts, links, forms and bookmarks untouched | Image downsampling (DPI cap) |
| **Modes** | Lossless / lossy, quality 1–100, effort (fast / balanced / maximum) | Per-engine algorithm settings, presets |
| **Conversion** | HEIC/PNG/WebP/AVIF/… → JPEG, PNG, WebP, AVIF, HEIC, TIFF, JPEG XR, BMP | |
| **Metadata** | Strip EXIF/XMP/IPTC/comments or keep them (JPEG, PNG, WebP, AVIF outputs) | Other formats |
| **Originals** | Recycle Bin by default, or kept with the result saved as `name-compressed.ext` | |
| **Integration** | Drag & drop, `smashit` command line | Explorer right-click menu, custom script engines |

### PDF

Smash It never rebuilds a PDF. qpdf exports its streams, jpegtran optimizes every JPEG image losslessly, and qpdf
writes the new image data back under the same object numbers. In lossy mode, qpdf first re-encodes images as JPEG
at the chosen quality, keeping each one only when it gets smaller. The result must pass `qpdf --check` and keep the
same number of pages, or the original is left alone. Signed PDFs (any change would invalidate the signature) and
encrypted PDFs are skipped.

### Metadata and originals

- **Strip metadata** (default) removes camera info, dates, GPS, XMP, IPTC and comments. The color profile and the
  orientation are always kept, since dropping them would change how the picture looks.
- **Keep originals** leaves your files untouched and writes `photo-compressed.jpg` (or `photo-compressed.webp` for a
  conversion) next to them. Without it, the compressed file replaces the original, which goes to the Recycle Bin.

Reading HEIC, WebP and AVIF relies on the free *HEIF*, *HEVC*, *WebP* and *AV1* image extensions from the Microsoft Store.

## Project layout

```
src/
  SmashIt.Core/    Compression engine: format detection, pipeline, compressors (no UI)
  SmashIt.App/     WinUI 3 desktop app (Windows App SDK, unpackaged, self-contained)
  SmashIt.Cli/     `smashit` command-line tool
tests/
  SmashIt.Core.Tests/
```

Every engine implements `ICompressor`. The `CompressionPipeline` asks each one whether it can handle a
job (input format, output format, lossless or lossy), runs the candidates and keeps the smallest output.
Adding a new engine (a bundled tool or your own script) means adding one class.

## Build

Requirements: Windows 10 1809+ / Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download),
and [7-Zip](https://www.7-zip.org/) to unpack jpegtran (`winget install 7zip.7zip`).

```powershell
# Download the bundled tools (pinned versions, SHA-256 verified) into tools\bin
powershell -ExecutionPolicy Bypass -File tools\fetch-tools.ps1

dotnet build SmashIt.slnx
dotnet test --solution SmashIt.slnx

# Run the app
dotnet run --project src/SmashIt.App

# Command line
dotnet run --project src/SmashIt.Cli -- --help
dotnet run --project src/SmashIt.Cli -- -q 75 "C:\Photos"
```

## Command line

```
smashit [options] <files or folders…>

  --lossless           Lossless only (default)
  --lossy              Allow quality loss
  -q, --quality <n>    Quality 1–100 (implies --lossy)
  -t, --to <format>    Convert to jpg, png, webp, avif, heic, tif, jxr, bmp
  -e, --effort <n>     fast, balanced (default) or max
  --keep               Keep the original, write "name-compressed.ext" next to it
  --no-recycle         Delete the original instead of using the Recycle Bin
  --keep-metadata      Keep EXIF, XMP, IPTC and comments
  --strip-metadata     Remove them (default; color profile and orientation are kept)
```

## License

[GPL-3.0-or-later](LICENSE). Bundled third-party tools keep their own licenses: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
