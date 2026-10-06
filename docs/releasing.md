# Releasing

## Publish a version

Tag the commit and push the tag:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The tag has to have the form `v<major>.<minor>.<patch>`. Pushing it starts the Release workflow, which runs the tests, builds both programs with that version and creates a GitHub release with two files:

| File | Contents |
|---|---|
| `WinCapture-1.2.0-Setup.exe` | the setup program. This is the download the README points to. |
| `WinCapture-1.2.0-x64.zip` | a `WinCapture` folder with `WinCapture.exe` and the `Editor` folder, for running without installing |

GitHub generates the release notes from the changes since the previous tag.

Neither file is signed, so Windows SmartScreen can warn about the download.

## Try a release before tagging

```powershell
scripts\release.ps1 -Version 1.2.0
```

This writes the same files to `artifacts\release`. It needs Inno Setup (`winget install JRSoftware.InnoSetup`) and stops a running WinCapture first, because that keeps its files locked.

## Continuous integration

The CI workflow runs on every push to `main` and on pull requests. It runs the tests and then `release.ps1` with a placeholder version, so a problem in the release build shows up before a tag is pushed. The files it builds are attached to the run as an artifact.

## The setup program

`packaging\WinCapture.iss` is an Inno Setup script. Setup installs for the current user only and needs no administrator: it copies the programs to `%LOCALAPPDATA%\Programs\WinCapture`, adds a Start menu entry and, unless unticked, an entry that starts WinCapture at sign-in. It closes a running WinCapture before replacing its files.

The uninstaller, under Settings, Apps, stops WinCapture, gives Print Screen back to Snipping Tool and removes what WinCapture registered. Settings and the log in `%LOCALAPPDATA%\WinCapture` are kept.

`Setup.exe /VERYSILENT` installs without showing a window. The pictures in the wizard come from `scripts\generate-assets.ps1`.
