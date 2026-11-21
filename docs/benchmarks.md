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
