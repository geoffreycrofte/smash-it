# Benchmarks

The measurements behind Smash It's settings and the figures in the README: what was tested, with which files and
commands, and what came out. Each engine or setting that relies on a measurement gets its section here, so the
results can be checked again when a tool is updated.

Sizes are given as the app shows them (1 MB = 1,048,576 bytes). Times are wall-clock times on the development
machine: a 6-core x64 processor (AVX2), Windows 11.

## AV1 video

*Measured on 2026-11-21, for v0.5.0.*

**Question:** which encoder and settings give the smallest videos that still look right, and what quality 70 should
mean for AV1.

**Files:** two 10-second 1080p H.264 clips (about 30 MB each) from test-videos.co.uk:
[Jellyfish](https://test-videos.co.uk/vids/jellyfish/mp4/h264/1080/Jellyfish_1080_10s_30MB.mp4) (camera footage) and
[Big Buck Bunny](https://test-videos.co.uk/vids/bigbuckbunny/mp4/h264/1080/Big_Buck_Bunny_1080_10s_30MB.mp4) (animation).

**Tools:** ffmpeg n9.0.2 (BtbN GPL build, the one bundled), SVT-AV1 4.3.0, x264, x265; Media Foundation through
the Smash It command line.

**Method:** each encode is compared to the source with VMAF (ffmpeg's `libvmaf` filter). VMAF scores how close a
video looks to the original, out of 100; above 93, differences are hard to see.

```sh
# Windows encoder, through Smash It
smashit --lossy -q 70 --codec h264 --keep clip.mp4
# SVT-AV1 at a given preset and CRF
ffmpeg -i clip.mp4 -c:v libsvtav1 -preset 7 -crf 34 -pix_fmt yuv420p10le -c:a copy out.mp4
# Score against the source
ffmpeg -i out.mp4 -i clip.mp4 -lavfi "[0:v]setpts=PTS-STARTPTS[d];[1:v]setpts=PTS-STARTPTS[r];[d][r]libvmaf=n_threads=6" -f null -
```

**Results at quality 70:**

| Clip | H.264 (Windows) | AV1 (SVT-AV1, preset 7, CRF 34) |
|---|---|---|
| Camera footage | 6.2 MB, VMAF 93.9, 2 s | **4.0 MB (−36 %)**, VMAF 92.6, 10 s |
| Animation | 6.0 MB, VMAF 84.5, 2 s | **3.9 MB (−36 %)**, VMAF 93.6, 11 s |

Other points measured on the camera clip: HEVC through Windows 3.7 MB at VMAF 90.3; x264 `slow` CRF 24 5.7 MB at
VMAF 91.0; x265 `medium` CRF 24 4.1 MB at VMAF 92.5. SVT-AV1 preset 7 makes files about 25 % smaller than preset 8,
for 40 % more time, which is why Balanced effort uses preset 7.

**Settings chosen:** quality 70 → CRF 34; Fast effort → preset 10, Balanced → 7, Maximum → 6; 10-bit output.

## H.264 and HEVC

*Measured on 2026-11-21, for the version after v0.5.0.*

**Question:** should H.264 and HEVC go through ffmpeg (x264, x265) rather than the Windows encoder, and at which CRF
and preset.

**Files and method:** the two clips of the [AV1 section](#av1-video), scored with VMAF the same way. Times include
decoding the source. The Windows encoder rows come from the AV1 measurements (Smash It at quality 70).

```sh
ffmpeg -i clip.mp4 -c:v libx264 -preset medium -crf 23 -c:a copy out.mp4
ffmpeg -i clip.mp4 -c:v libx265 -preset medium -crf 24 -tag:v hvc1 -c:a copy out.mp4
```

**Results:**

| Encoder | Camera footage | Animation |
|---|---|---|
| Windows H.264, quality 70 | 6.2 MB, VMAF 93.9, 2 s | 6.0 MB, **VMAF 84.5**, 2 s |
| x264 `medium` CRF 22 | 7.7 MB, VMAF 93.6, 8.7 s | 8.3 MB, VMAF 94.0, 8.0 s |
| x264 `medium` CRF 23 | 6.8 MB, VMAF 92.5, 8.0 s | 7.1 MB, VMAF 93.3, 8.3 s |
| x264 `medium` CRF 25 | 5.4 MB, VMAF 89.7, 7.1 s | 5.2 MB, VMAF 91.6, 7.8 s |
| x264 `slow` CRF 23 | 6.4 MB, VMAF 92.3, 11.8 s | 7.1 MB, VMAF 93.5, 11.3 s |
| Windows HEVC, quality 70 | 3.7 MB, VMAF 90.3 | 3.6 MB, **VMAF 81.9** |
| x265 `medium` CRF 24 | 4.1 MB, VMAF 92.5, 15.3 s | 5.8 MB, VMAF 93.7, 11.2 s |
| x265 `medium` CRF 26 | 3.2 MB, VMAF 89.8, 13.1 s | 4.2 MB, VMAF 92.1, 10.9 s |

**What it shows:** x264 is not smaller than the Windows encoder; on the camera clip, the Windows encoder even gives
a slightly better score for its size, 4 to 6 times faster. But it targets a bitrate, and the detailed animation
falls to VMAF 84.5 (82 in HEVC), where blur shows. At constant quality, x264 and x265 stay around 93 on both clips.
`slow` saves 0 to 5 % over `medium` for 40 % more time.

**Settings chosen:** ffmpeg is the default engine for H.264 and HEVC, the Windows encoder an option for speed (a
choice the user made on these numbers). x264 CRF 16 / 23 / 30 at quality 100 / 70 / 40; x265 CRF 18 / 24 / 30;
presets `veryfast` / `medium` / `slow` by effort; 8-bit output, which every player reads.

## Animated GIF to video

*Measured on 2026-11-21, for the version after v0.5.0.*

**Question:** how much an animated GIF gains as a video, compared with gifsicle, and which encoder settings to use.

**Files:** three 4-second animated GIFs at 15 frames per second, made with ffmpeg's palette filters (the usual
way GIFs are made from a video):

```sh
# From the two clips above, 2 s in, 480 px wide
ffmpeg -ss 2 -t 4 -i clip.mp4 -vf "fps=15,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse" clip.gif
# A screen recording: a screenshot scrolled at 60 px per second, 800×450, no dithering
ffmpeg -loop 1 -t 4 -i screenshot.png -vf "fps=15,scale=800:-1,crop=800:450:0:'min(t*60,ih-450)',split[a][b];[a]palettegen[p];[b][p]paletteuse=dither=none" screen.gif
```

**Tools:** ffmpeg n9.0.2 (x264, libvpx-vp9), gifsicle 1.97, through the Smash It command line.

**Method:** `smashit -q 70 --keep file.gif` (gifsicle), then `--gif mp4` and `--gif webm`. A dozen frames were
compared side by side, zoomed in, between the GIF and the video. Colors were checked with an RGB PSNR between the
GIF and the decoded MP4 of the screen recording: 43.9 dB when decoded as BT.709, as tagged, against 41.6 dB as
BT.601, so the color conversion and its tags agree.

**Results at quality 70:**

| GIF | gifsicle (lossy) | MP4 (x264 `slow`, CRF 23) | WebM (VP9, CRF 34) |
|---|---|---|---|
| Animation clip, 5.23 MB | no gain | **289 KB (−95 %)** | **164 KB (−97 %)** |
| Camera clip, 4.85 MB | 3.23 MB (−33 %) | **364 KB (−93 %)** | **331 KB (−93 %)** |
| Screen recording, 1.23 MB | 599 KB (−52 %) | **21.8 KB (−98 %)** | **12.4 KB (−99 %)** |

Each encode takes under 2 seconds. CRF sweeps (x264 20–28, VP9 30–38) went from 512 KB to 115 KB on the animation
clip; CRF 23 is x264's default and kept the frames visually on par with the GIF.

**Timing check:** a GIF optimized by gifsicle merges identical frames into one longer frame. With B-frames, an MP4
loses how long its last frame lasts: the screen recording, which ends on a long pause, came out 2.1 s long instead
of 4 s. Writing the MP4 at a constant frame rate fixes it (4.0 s, 42 KB instead of 34 KB); turning B-frames off
also does, but made the animation clip 59 % larger. WebM keeps each frame's duration as it is.

**Settings chosen:** x264 CRF from 16 (quality 100) to 23 (70) and 30 (40), preset `veryfast` / `slow` /
`veryslow` by effort, constant frame rate; VP9 CRF from 24 to 34 and 44, `cpu-used` 4 / 2 / 1.

## PowerPoint

*Measured on 2026-11-21, for the version after v0.5.0.*

**Question:** what Smash It saves on a typical deck of photos, and whether the files stay valid.

**Files:** a deck made with the Open XML SDK (the test helper `tests/SmashIt.Core.Tests/TestPresentation.cs`): five
3000×2000 JPEG photos (650 KB to 1.3 MB each, 4.58 MB in all), one per 16:9 slide, shown 13.33, 6, 4, 13.33 and 6
inches wide. No PowerPoint or LibreOffice was available, so the results were checked with the SDK's validator
(Office 2019 rules) rather than opened in PowerPoint.

**Tools:** the Smash It command line (WIC, jpegtran, the presentation engine).

```sh
smashit --lossless --keep photos.pptx
smashit -q 75 --pdf-dpi 150 --keep photos.pptx
```

**Results:**

| Settings | Result | Time |
|---|---|---|
| Lossless | 4.58 MB (−0.1 %) | 3.6 s |
| Lossy, quality 75, no resolution limit | 4.58 MB (−0.1 %) | 3.4 s |
| Lossy, quality 75, 300 dpi | 2.86 MB (−38 %) | 2.8 s |
| Lossy, quality 75, 150 dpi | **1.17 MB (−75 %)** | 2.2 s |
| Lossy, quality 60, 150 dpi | **896 KB (−81 %)** | 2.2 s |

At 150 dpi the pictures came out 2000×1333 (13.33 inches), 900×600 (6 inches) and 600×400 (4 inches), as expected.
All five results pass the validator. These photos were already well compressed, so re-encoding alone gains
nothing: the gain comes from scaling pictures to the size they are shown at. Photos straight from a phone (often
3–5 MB) would also gain from the re-encoding.

**Keynote:** no `.key` file was available. The handling follows the format as documented by
[keynote-parser](https://github.com/psobot/keynote-parser): a ZIP whose entries are stored uncompressed, with the
pictures in `Data/`, replaceable at the same pixel size. The tests cover it with a package built that way.

## Sound

*Measured on 2026-11-21, for the version after v0.5.0.*

**Question:** what each codec saves on music and on speech, and which bitrates to use.

**Files:**

- **Music:** a 60-second excerpt (from 1:00) of a CD-quality rock song in FLAC, 44.1 kHz stereo. It is "Cold Feet" by John Russell, from the album
  [Dirt Nap](https://archive.org/details/pandacd-3978-dirt-nap) on Internet Archive (CC BY-NC-ND 3.0, used only for
  measuring), decoded to WAV.
- **Voice:** a 60-second excerpt (from 1:00), copied without re-encoding, of the LibriVox audiobook chapter
  [William Again, chapter 1](https://archive.org/details/williamagain_1902_librivox) (public domain). It is a 128 kbit/s
  mono MP3: the kind of file podcasts and audiobooks come as.
- **Synthetic voice:** 31 seconds of English speech from the Windows speech synthesizer (`System.Speech`), saved as a
  44.1 kHz mono WAV.

**Tools:** ffmpeg n9.0.2 (LAME, libopus, libvorbis, ffmpeg's own AAC and FLAC encoders), through the Smash It command
line.

```sh
smashit --lossless --keep music.wav voice.mp3            # FLAC; tags only for the MP3
smashit -q 70 -t opus --keep music.wav                   # music, Opus
smashit -q 70 --voice -t m4a --keep voice.mp3            # speech, AAC
```

**Results at quality 70** (the music as Music, the speech as Voice):

| File | FLAC | MP3 | AAC (M4A) | Opus |
|---|---|---|---|---|
| Music, WAV 10.09 MB | 5.22 MB (−48 %) | 1.42 MB (−86 %), V2 | 1.18 MB (−88 %), 160 kbit/s | **763 KB (−93 %)**, 96 kbit/s |
| Voice, MP3 939 KB | — | 469 KB (−50 %), 64 kbit/s | 487 KB (−48 %), 64 kbit/s | **244 KB (−74 %)**, 32 kbit/s |
| Synthetic voice, WAV 2.65 MB | 797 KB (−71 %) | 247 KB (−91 %) | 235 KB (−91 %) | **118 KB (−96 %)** |

Each file takes well under a second. FLAC at its strongest setting (Maximum effort, level 12) saves another 4 % on
the music (5.02 MB). The 128 kbit/s mono MP3, treated as music, is copied rather than re-encoded: mono music at
quality 70 needs about 114 kbit/s, too close to what it already has to be worth a generation of loss. In lossless
mode it only loses its tags (about 100 bytes here).

Bitrate sweeps on the music (Opus 64/96/128 kbit/s: 510/763/1025 KB; AAC 128/160/192 kbit/s; LAME V0/V2/V4/V5:
1.89/1.42/1.14/0.96 MB) and the speech (Opus 16/24/32/48 kbit/s) gave the sizes the bitrates predict.

**Quality:** not measured. ffmpeg has no perceptual score for sound like VMAF for video, and no listening test was
run. The bitrates follow the codec authors' recommendations: Opus at 96 kbit/s for stereo music and 32 kbit/s for
speech, LAME V2 for music. Quality 70 sits on those points, and the slider moves around them.

**Settings chosen:** music at quality 100 / 70 / 40: Opus 160 / 96 / 64 kbit/s, AAC 256 / 160 / 112 kbit/s, MP3 V0 /
V2 / V5, Vorbis q8 / q5 / q3. Voice (mono): Opus 48 / 32 / 24 kbit/s, AAC and MP3 96 / 64 / 48 kbit/s. Mono music
gets 60 % of the stereo bitrate.

## Slim ffmpeg build

*Measured on 2026-11-22, for the version after v0.5.0.*

**Question:** does an ffmpeg built with only what Smash It uses (`tools/ffmpeg-configure.sh`, static, MSYS2 UCRT64)
give the same results as the full BtbN build it replaces, and how much does it save?

**Size:** 160.5 MB → **51.5 MB** for `ffmpeg.exe`; compressed with LZMA2 at the strongest level (what the installer
does), 41.0 MB → **9.8 MB**. Measured on the packages themselves: the installer goes from 98.8 MB (v0.5.0) to
**65.7 MB**, the portable zip from 152.5 MB to **101.0 MB**. One exe, no DLL.

**Same results:** every test passes with it (268), and the encodes of the sections above, run with each build through
the Smash It command line at quality 70, came out the same:

| Encode | BtbN build | Slim build |
|---|---|---|
| AV1, camera clip | 4,166,689 bytes, VMAF 92.63, 11.7 s | 4,166,689 bytes, VMAF 92.63, 10.3 s |
| H.264 (x264), animation clip | 7,463,941 bytes, VMAF 93.30, 7.9 s | 7,463,956 bytes, VMAF 93.30, 8.0 s |
| HEVC (x265), camera clip | 4,254,589 bytes, VMAF 92.52, 14.2 s | 4,254,562 bytes, VMAF 92.52, 14.6 s |
| Animated GIF → MP4 | 296,406 bytes | 296,421 bytes |
| Animated GIF → WebM | 168,410 bytes | 168,410 bytes |
| Music → Opus / MP3 / AAC | 780,942 / 1,484,000 / 1,241,173 bytes | 782,844 / 1,484,005 / 1,241,173 bytes |
| Music → FLAC | 5,472,016 bytes | 5,472,016 bytes |

The few bytes apart come from the library versions written in the files (the Opus file differs by 0.2 %). The
library versions are listed in the release's `libraries.txt`.

## libvips and WIC

*Measured on 2026-11-22, for the macOS and Linux versions (phase 0 of `docs/cross-platform-plan.md`).*

**Question:** on macOS and Linux, libvips reads and writes the images where Windows uses WIC. Do the same files and
settings give the same results?

**Files:**
- **The [Kodak set](http://r0k.us/graphics/kodak/):** 24 photos, 768×512, uncompressed PNG, 14.68 MB.
- **Three large photos,** 5000×3333 JPEG, 5.83 MB in all, from Unsplash through Picsum:
  [1015](https://unsplash.com/photos/-oWyJoSqBRM), [1043](https://unsplash.com/photos/mWRR1xj95hg) and
  [1050](https://unsplash.com/photos/xcC5ozHk_N8) (`https://picsum.photos/id/<id>/5000/3333.jpg`).

**Tools:** the Smash It command line, built twice on the same Windows machine:
- for the Windows target, so with WIC;
- for plain .NET, so with libvips 8.18.7 (NetVips.Native), as on macOS and Linux.

Both builds use the same bundled tools.

**Method:** each case runs both builds on a fresh copy of the files and adds up the output sizes. Times are for the
whole batch and include starting the program; each figure is the median of three runs. Quality is the mean PSNR
against the source PNG, over the RGB channels, computed with libvips.

```sh
smashit --folder kodak/*                                  # lossless PNG
smashit --folder --lossy -q 80 -t jpg kodak/*             # PNG to JPEG
smashit --folder --lossy -q 80 -t webp kodak/*            # PNG to WebP, through the intermediate PNG
smashit --folder --lossy -q 80 --max-size 2560 large/*    # large JPEGs scaled down
```

**Results:**

| Case | WIC | libvips |
|---|---|---|
| Lossless PNG (oxipng wins) | 14,709,218 bytes, 4.9 s | 14,709,218 bytes, 4.4 s |
| PNG → JPEG, quality 80 | 1,774,627 bytes, PSNR 35.370 dB, 1.4 s | 1,774,627 bytes, PSNR 35.370 dB, 1.2 s |
| PNG → WebP, quality 80 | 1,434,822 bytes, 1.7 s | 1,434,822 bytes, 1.5 s |
| 5000 px JPEG → 2560 px, quality 80 | 2,646,517 bytes, 1.3 s | 2,746,423 bytes (**+3.8 %**), 0.9 s |

**What it shows:**
- **Same results at the same size:**
  - **JPEG:** both engines use libjpeg-turbo with the same quantization tables. The files differ only by 3 header
    bytes (the declared resolution: WIC writes 96 dpi).
  - **WebP:** the files are identical byte for byte, since the intermediate PNG handed to cwebp holds the same pixels.
  - **Lossless PNG:** oxipng's result wins on both systems.
- **Scaling down differs:** WIC averages pixels (Fant), libvips uses Lanczos3. Lanczos3 keeps more fine detail, so
  the JPEGs are about 4 % heavier. Windows keeps WIC, so its results don't change; the macOS and Linux versions will
  be a little sharper and a little heavier at the same settings.
- **libvips is a little faster here,** 10 to 30 % depending on the case.

## Earlier measurements

These figures appear in the README; their test files and commands weren't recorded at the time. They will be
measured again, with the method written down here, when their engine next changes.

| Setting | Figure |
|---|---|
| Image maximum size | A 5000 px photo loses about half its weight at 2560 px and three quarters at 1920 px |
| Maximum colors | A photo PNG at 64 colors: −34 % |
| Dithering off | A lossy screenshot: −69 % |
| Zopfli | A screenshot: −9 % |
| Video maximum resolution | A 4K phone video at 1080p: 3 to 4 times lighter |
| PDF image resolution | A 600 dpi scan at 150 dpi: −80 to −95 % |
