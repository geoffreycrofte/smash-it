# Code signing policy

> **Status:** Smash It has applied to the SignPath Foundation's free code signing program for open source
> projects. Until it is accepted, releases are not signed and Windows SmartScreen may warn before the installer
> runs. This page will say when signed releases start.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

## What gets signed

- Smash It's own programs and libraries: `SmashIt.exe`, `SmashIt.dll`, `SmashIt.Cli.exe`, `SmashIt.Cli.dll` and
  `SmashIt.Core.dll`.
- The installer, `SmashIt-Setup-x64.exe`. The portable zip contains the same signed files.

The bundled tools (oxipng, pngquant, gifsicle, oxvg, jpegtran, cwebp, avifenc, qpdf) are other open source
projects. They are included as their authors publish them, and are never signed with this certificate. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## How a release is signed

Every release is built from this repository's source code by the
[Release workflow](.github/workflows/release.yml), on GitHub-hosted runners. The workflow sends Smash It's own
files, then the installer, to SignPath. Each signing request must be approved by hand before anything is signed.

## Team roles

- Committers and reviewers: [Geoffrey Crofte](https://github.com/geoffreycrofte)
- Approvers: [Geoffrey Crofte](https://github.com/geoffreycrofte)

Changes from anyone else come through pull requests and are reviewed by a committer before they are merged.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating it, with this one exception: at startup, Smash It asks GitHub for the
list of its releases (`api.github.com`) to tell you when a new version is out. Nothing about you or your files is
sent. You can turn this off under **Settings → About → Check for updates at startup**. When you choose to see
what's new or to update, it downloads the notes (`raw.githubusercontent.com`) and the installer (`github.com`).

Your files are compressed on your computer and never leave it.
