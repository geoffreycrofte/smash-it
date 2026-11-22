#!/usr/bin/env bash
# Builds the bundled tools that have no official macOS or Linux build: pngquant, gifsicle, oxvg and jpegtran.
# Run by the "Build tools (macOS, Linux)" workflow (.github/workflows/tools-unix.yml) on macOS (Apple Silicon) and
# Linux (x64, Ubuntu 22.04: the oldest glibc Smash It supports). Each tool lands in dist/ as
# <tool>-<version>-<platform>.tar.gz, with its license; the libraries it needs beyond the system's are linked in.
#
#   tools/build-unix-tools.sh macos-arm64|linux-x64
set -euo pipefail

platform=${1:?usage: build-unix-tools.sh macos-arm64|linux-x64}
PNGQUANT_VERSION=2.17.0
GIFSICLE_VERSION=1.97
OXVG_VERSION=0.0.9
LIBJPEG_TURBO_VERSION=3.2.1

root=$(pwd)
work="$root/build"
dist="$root/dist"
mkdir -p "$work" "$dist"
jobs=$(getconf _NPROCESSORS_ONLN)

case "$platform" in
  macos-arm64)
    # The oldest macOS Smash It supports, and Homebrew's static libpng (macOS has none of its own).
    export MACOSX_DEPLOYMENT_TARGET=15.0
    libpng_include="$(brew --prefix libpng)/include"
    libpng_static="$(brew --prefix libpng)/lib/libpng16.a"
    ;;
  linux-x64)
    libpng_include=/usr/include
    libpng_static=/usr/lib/x86_64-linux-gnu/libpng16.a
    ;;
  *) echo "Unknown platform $platform"; exit 1 ;;
esac

# Strips, re-signs on macOS (Apple Silicon refuses to run an unsigned binary: an ad hoc signature is enough), checks
# the program starts and lists what it links to, then packs it with its license.
package() { # tool version binary license [arguments to check it runs]
  local tool=$1 version=$2 binary=$3 license=$4; shift 4
  local folder="$work/package/$tool"
  rm -rf "$folder"; mkdir -p "$folder"
  cp "$binary" "$folder/$tool"
  strip "$folder/$tool"
  if [ "$platform" = macos-arm64 ]; then
    codesign --force --sign - "$folder/$tool"
    otool -L "$folder/$tool"
    # Only the system's own libraries: nothing from Homebrew.
    if otool -L "$folder/$tool" | tail -n +2 | grep -v -E '^\s*/(usr/lib|System)/'; then
      echo "$tool links to a library outside the system"; exit 1
    fi
  else
    ldd "$folder/$tool" || true
    # Only glibc, zlib and the C++ runtime: anything else may be missing on the user's system.
    if ldd "$folder/$tool" | grep -v -E '(linux-vdso|ld-linux|libc\.so|libm\.so|libz\.so|libgcc_s\.so|libstdc\+\+\.so|libpthread\.so|libdl\.so)'; then
      echo "$tool links to a library outside the base system"; exit 1
    fi
  fi
  "$folder/$tool" "$@" > /dev/null
  cp "$license" "$folder/LICENSE.txt"
  tar -czf "$dist/$tool-$version-$platform.tar.gz" -C "$folder" "$tool" LICENSE.txt
}

# pngquant (GPL-3.0): libimagequant is its "lib" submodule; libpng is linked in statically.
git clone --quiet --depth 1 --recurse-submodules --shallow-submodules --branch "$PNGQUANT_VERSION" \
  https://github.com/kornelski/pngquant.git "$work/pngquant"
(
  cd "$work/pngquant"
  ./configure --without-lcms2 --extra-cflags="-I$libpng_include"
  make -j"$jobs"
  # configure always links the shared libpng it finds: the program is linked again, with the static one.
  rm pngquant
  cc pngquant.o pngquant_opts.o rwpng.o ./lib/libimagequant.a "$libpng_static" -lz -lm -o pngquant
)
package pngquant "$PNGQUANT_VERSION" "$work/pngquant/pngquant" "$work/pngquant/COPYRIGHT" --version

# gifsicle (GPL-2.0): no dependencies; gifview (an X11 viewer) is left out.
curl -fsSL "https://www.lcdf.org/gifsicle/gifsicle-$GIFSICLE_VERSION.tar.gz" | tar -xz -C "$work"
(
  cd "$work/gifsicle-$GIFSICLE_VERSION"
  ./configure --disable-gifview --disable-gifdiff
  make -j"$jobs"
)
package gifsicle "$GIFSICLE_VERSION" "$work/gifsicle-$GIFSICLE_VERSION/src/gifsicle" "$work/gifsicle-$GIFSICLE_VERSION/COPYING" --version

# oxvg (MIT): Rust, from its tag.
git clone --quiet --depth 1 --branch "v$OXVG_VERSION" https://github.com/noahbald/oxvg.git "$work/oxvg"
(
  cd "$work/oxvg"
  cargo build --locked --release --package oxvg --bins
)
package oxvg "$OXVG_VERSION" "$work/oxvg/target/release/oxvg" "$work/oxvg/LICENSE" --version

# jpegtran (libjpeg-turbo: IJG, BSD-3-Clause, zlib): static library, no shared one.
git clone --quiet --depth 1 --branch "$LIBJPEG_TURBO_VERSION" https://github.com/libjpeg-turbo/libjpeg-turbo.git "$work/libjpeg-turbo"
cmake -S "$work/libjpeg-turbo" -B "$work/libjpeg-turbo/out" -DCMAKE_BUILD_TYPE=Release \
  -DENABLE_SHARED=OFF -DWITH_TURBOJPEG=OFF -DWITH_TESTS=OFF
cmake --build "$work/libjpeg-turbo/out" -j"$jobs"
jpegtran=$(find "$work/libjpeg-turbo/out" -maxdepth 1 -type f -name 'jpegtran*' -perm -u+x | head -1)
package jpegtran "$LIBJPEG_TURBO_VERSION" "$jpegtran" "$work/libjpeg-turbo/LICENSE.md" -version

ls -l "$dist"
( cd "$dist" && shasum -a 256 *.tar.gz )
