# Smash It

A native Windows app to make images, videos and PDFs smaller — in the spirit of ImageOptim on macOS.

Drop files or folders on the window: Smash It tries every engine that can handle them, keeps the smallest
result, and only touches your file when it actually got smaller. Originals go to the Recycle Bin by default.

> Status: early development (v0.1). Image compression through Windows' own codecs works today;
> see the roadmap below for what's coming.

## Features

| | Today | Planned |
|---|---|---|
| **Images** | JPEG, PNG, TIFF, BMP, JPEG XR, HEIC via the Windows Imaging Component | Lossless optimizers (oxipng, mozjpeg/jpegtran, gifsicle, svgo), WebP/AVIF encoding |
| **Videos** | — | H.264/HEVC through Media Foundation (hardware encoders), ffmpeg for fine tuning |
| **PDF** | — | Recompress embedded images in place without touching text, fonts, links or forms |
| **Modes** | Lossless / lossy, quality 1–100 | Per-engine algorithm settings, presets |
| **Conversion** | HEIC/PNG/WebP/AVIF/… → JPEG, PNG, TIFF, HEIC, JPEG XR, BMP | → WebP, AVIF |
| **Integration** | Drag & drop, `smashit` command line | Explorer right-click menu, custom script engines |

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

Requirements: Windows 10 1809+ / Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
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
  -t, --to <format>    Convert to jpg, png, tif, bmp, jxr, heic…
  --keep               Keep the original, write "name.smashed.ext" next to it
  --no-recycle         Overwrite the original instead of using the Recycle Bin
```

## License

[GPL-3.0-or-later](LICENSE). Smash It will bundle third-party tools under their own licenses (GPL, LGPL, MIT, BSD…).
