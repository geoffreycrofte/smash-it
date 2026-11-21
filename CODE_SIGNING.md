# Code signing policy

> **Status:** Smash It's releases are **not code-signed** for now. Windows SmartScreen may show "Windows protected
> your PC" before the installer runs: click **More info**, then **Run anyway**. To check that a download is the one
> this repository built, compare its SHA-256 with `SHA256SUMS.txt`, attached to every release.

The release workflow is ready to sign through the [SignPath Foundation](https://signpath.org)'s free program for
open source projects, which requires a project with a wider following than Smash It has today. This page describes
how signing will work once it starts.

## What will be signed

- Smash It's own programs and libraries: `SmashIt.exe`, `SmashIt.dll`, `SmashIt.Cli.exe`, `SmashIt.Cli.dll` and
  `SmashIt.Core.dll`.
- The installer, `SmashIt-Setup-x64.exe`. The portable zip will contain the same signed files.

The bundled tools (oxipng, pngquant, gifsicle, oxvg, jpegtran, cwebp, avifenc, qpdf) are other open source
projects. They are included as their authors publish them, and will never be signed with this certificate. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## How a release is built

Every release is built from this repository's source code by the
[Release workflow](.github/workflows/release.yml), on GitHub-hosted runners. Once signing is enabled, the workflow
will send Smash It's own files, then the installer, to SignPath, and each signing request will have to be approved
by hand.

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
