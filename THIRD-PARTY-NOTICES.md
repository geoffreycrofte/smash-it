# Third-party notices

Smash It bundles the following command-line tools, unmodified, in its `tools` folder.
They are downloaded by `tools/fetch-tools.ps1` from their official releases and verified by SHA-256.

| Tool | Version | Used for | License | Source |
|---|---|---|---|---|
| oxipng | 10.2.1 | Lossless PNG optimization | MIT | https://github.com/oxipng/oxipng |
| pngquant | 2.17.0 | Lossy PNG compression (palette of up to 256 colors) | GPL-3.0-or-later | https://github.com/kornelski/pngquant (binary: https://pngquant.org) |
| gifsicle | 1.97 | GIF optimization, animated GIFs included | GPL-2.0-or-later | https://github.com/kohler/gifsicle (Windows build: https://eternallybored.org/misc/gifsicle/) |
| jpegtran (libjpeg-turbo) | 3.2.1 | Lossless JPEG optimization | IJG License, Modified BSD, zlib | https://github.com/libjpeg-turbo/libjpeg-turbo |
| cwebp (libwebp) | 1.6.0 | WebP encoding | BSD-3-Clause | https://chromium.googlesource.com/webm/libwebp |
| qpdf | 12.4.2 | PDF stream rewriting and image optimization | Apache-2.0 (ships the Microsoft Visual C++ runtime as an app-local redistributable) | https://github.com/qpdf/qpdf |
| avifenc (libavif) | 1.4.2 | AVIF encoding | BSD-2-Clause (statically linked codecs: libaom BSD-2-Clause + AOM patent license, dav1d BSD-2-Clause) | https://github.com/AOMediaCodec/libavif |

This software is based in part on the work of the Independent JPEG Group.

License texts shipped with the tools are copied to `tools/licenses/`.
