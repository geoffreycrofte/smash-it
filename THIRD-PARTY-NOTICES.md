# Third-party notices

Smash It bundles the following command-line tools in its `tools` folder: unmodified official releases, except
oxvg and ffmpeg, built from their source by this repository (see each row). `tools/fetch-tools.ps1` downloads them
and verifies each one by SHA-256.

| Tool | Version | Used for | License | Source |
|---|---|---|---|---|
| oxipng | 10.2.1 | Lossless PNG optimization | MIT | https://github.com/oxipng/oxipng |
| pngquant | 2.17.0 | Lossy PNG compression (palette of up to 256 colors) | GPL-3.0-or-later | https://github.com/kornelski/pngquant (binary: https://pngquant.org) |
| gifsicle | 1.97 | GIF optimization, animated GIFs included | GPL-2.0-or-later | https://github.com/kohler/gifsicle (Windows build: https://eternallybored.org/misc/gifsicle/) |
| oxvg | 0.0.9 | SVG optimization | MIT | https://github.com/noahbald/oxvg (Windows x64 build compiled from the v0.0.9 tag by this repository's *Build tools* workflow) |
| jpegtran (libjpeg-turbo) | 3.2.1 | Lossless JPEG optimization | IJG License, Modified BSD, zlib | https://github.com/libjpeg-turbo/libjpeg-turbo |
| cwebp (libwebp) | 1.6.0 | WebP encoding | BSD-3-Clause | https://chromium.googlesource.com/webm/libwebp |
| qpdf | 12.4.2 | PDF stream rewriting and image optimization | Apache-2.0 (ships the Microsoft Visual C++ runtime as an app-local redistributable) | https://github.com/qpdf/qpdf |
| ffmpeg | 9.0.2 | Video (H.264, HEVC, AV1, VP9), sound (MP3, AAC, Opus, Vorbis, FLAC), animated GIF to video | GPL-3.0-or-later. Static build linking x264 and x265 (GPL-2.0-or-later), SVT-AV1 (BSD-3-Clause-Clear + AOM patent license), libvpx (BSD-3-Clause), dav1d (BSD-2-Clause), LAME (LGPL-2.0-or-later), mpg123 (LGPL-2.1-or-later), Opus, Vorbis and Ogg (BSD-3-Clause), zlib (Zlib), and the MinGW-w64 runtimes | https://ffmpeg.org. Built by this repository's "Build tools" workflow with only what Smash It uses (`tools/ffmpeg-configure.sh`); the `tools-ffmpeg-9.0.2-slim` release carries the FFmpeg source and the MSYS2 source package of every library |
| avifenc (libavif) | 1.4.2 | AVIF encoding | BSD-2-Clause (statically linked codecs: libaom BSD-2-Clause + AOM patent license, dav1d BSD-2-Clause) | https://github.com/AOMediaCodec/libavif |

This software is based in part on the work of the Independent JPEG Group.

License texts shipped with the tools are copied to `tools/licenses/`.
