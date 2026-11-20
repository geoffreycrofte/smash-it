# winget

Smash It's package for the Windows Package Manager: `winget install GeoffreyCrofte.SmashIt`, once the first
version is accepted in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

## The first version (by hand, once)

`manifests/g/GeoffreyCrofte/SmashIt/0.4.1` holds the four manifest files for v0.4.1, in the layout winget-pkgs
uses. Check them, then submit them:

```powershell
winget validate --manifest winget/manifests/g/GeoffreyCrofte/SmashIt/0.4.1
```

Submit them as a pull request to winget-pkgs, from a fork, with the same folder path. The winget bot checks the
manifests, installs the app in a virtual machine and asks you to accept the Microsoft CLA. A maintainer then
merges the pull request, usually within a few days.

## Later versions (automatic)

The [winget workflow](../.github/workflows/winget.yml) runs when a release is published (pre-releases are
skipped). It uses [wingetcreate](https://github.com/microsoft/winget-create) to open the pull request for the new
version, with the installer's URL and checksum. It needs a `WINGET_TOKEN` repository secret: a classic GitHub
token with the `public_repo` scope, from the account that submits. Without the secret, or before the first
version is merged, the workflow stops with a warning and doesn't fail.

To submit a release again by hand: **Actions → winget → Run workflow**, with its tag.

## Install details

- One installer for both scopes: winget adds `/CURRENTUSER` (the default, no admin prompt) or `/ALLUSERS`
  (`winget install GeoffreyCrofte.SmashIt --scope machine`).
- A silent install keeps the installer's default tasks: the Explorer right-click entry is added, the desktop icon
  and the `smashit` command on the PATH are not.
- `ProductCode` is the Inno Setup `AppId` followed by `_is1`, which lets winget find an existing install and
  upgrade it.
