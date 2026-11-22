#!/usr/bin/env bash
# Configures FFmpeg with only what Smash It uses, for a static Windows x64 build in MSYS2 UCRT64.
# Run from the FFmpeg source folder (the "Build tools" workflow does: .github/workflows/tools.yml).
#
# MSYS2 builds LAME with mpg123 (its decoding side), so the static libmp3lame needs libmpg123 (and shlwapi) too.
#
# Reading: broad, since people drop all kinds of videos and sounds. Writing: only the encoders and containers the
# engines produce, plus what the tests use to make their samples (PCM WAV, MJPEG cover art, GIF, lavfi sources).
set -euo pipefail

list() { local IFS=,; echo "$*"; }

decoders=(
  # Video
  h264 hevc libdav1d vp8 vp9 mpeg4 msmpeg4v2 msmpeg4v3 h263 mpeg1video mpeg2video mjpeg prores dnxhd dvvideo
  rawvideo theora gif png wrapped_avframe
  # Sound
  aac aac_latm mp3 mp3float mp2 flac alac opus vorbis ac3 eac3 dca amrnb amrwb adpcm_ima_wav adpcm_ms
  pcm_s16le pcm_s16be pcm_s24le pcm_s24be pcm_s32le pcm_s32be pcm_f32le pcm_f32be pcm_f64le pcm_f64be pcm_u8
  pcm_alaw pcm_mulaw
)
encoders=(
  libx264 libx265 libsvtav1 libvpx_vp9 aac libmp3lame libopus libvorbis flac
  # macOS and Linux: HEIC photos are decoded to a PNG for libvips (its build has no HEVC decoder)
  png
  # Tests, and "-f null"
  pcm_s16le pcm_f32le mjpeg gif wrapped_avframe
)
demuxers=(mov matroska avi gif mp3 flac ogg wav aiff aac lavfi)
muxers=(mp4 ipod webm mp3 flac ogg opus wav null md5 gif image2)
parsers=(h264 hevc av1 vp8 vp9 mpeg4video mpegvideo mjpeg png gif aac aac_latm mpegaudio flac opus vorbis ac3 dca)
bsfs=(aac_adtstoasc extract_extradata vp9_superframe vp9_superframe_split av1_frame_merge av1_frame_split
      h264_mp4toannexb hevc_mp4toannexb dump_extradata null)
filters=(
  # Graph plumbing, and what ffmpeg inserts itself (-ss/-t trims, autorotation, frame rate, pixel formats)
  buffer buffersink abuffer abuffersink null anull copy format aformat scale aresample trim atrim setpts asetpts
  transpose hflip vflip fps
  # Smash It's own filter graphs
  setparams setsar split palettegen paletteuse alphaextract signalstats metadata
  # Test samples
  testsrc2 color sine anoisesrc tpad drawbox join
)

./configure \
  --prefix=/usr/local --arch=x86_64 --target-os=mingw32 \
  --enable-gpl --enable-static --disable-shared \
  --pkg-config-flags=--static --extra-ldflags="-static -static-libgcc -static-libstdc++" \
  --extra-libs="-lmpg123 -lshlwapi" \
  --disable-autodetect --disable-everything --disable-doc --disable-ffplay --disable-ffprobe --disable-network \
  --enable-zlib \
  --enable-libx264 --enable-libx265 --enable-libsvtav1 --enable-libvpx --enable-libmp3lame \
  --enable-libopus --enable-libvorbis --enable-libdav1d \
  --enable-swscale --enable-swresample --enable-avfilter --enable-avdevice \
  --enable-indev=lavfi \
  --enable-protocol=file,pipe \
  --enable-decoder="$(list "${decoders[@]}")" \
  --enable-encoder="$(list "${encoders[@]}")" \
  --enable-demuxer="$(list "${demuxers[@]}")" \
  --enable-muxer="$(list "${muxers[@]}")" \
  --enable-parser="$(list "${parsers[@]}")" \
  --enable-bsf="$(list "${bsfs[@]}")" \
  --enable-filter="$(list "${filters[@]}")"
