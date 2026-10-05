# Releasing

## Publish a version

Tag the commit and push the tag:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The tag has to have the form `v<major>.<minor>.<patch>`. Pushing it starts the Release workflow, which runs the tests, builds both programs with that version and creates a GitHub release with one file:

| File | Contents |
|---|---|
| `WinCapture-1.2.0-x64.zip` | a `WinCapture` folder with `WinCapture.exe` and the `Editor` folder |

GitHub generates the release notes from the changes since the previous tag.

The programs are not signed and there is no setup program yet. Windows SmartScreen can therefore warn about the download, and WinCapture does not start with Windows unless the user sets that up.

## Try a release before tagging

```powershell
scripts\release.ps1 -Version 1.2.0
```

This writes the same file to `artifacts\release`. It stops a running WinCapture first, because that keeps its files locked.

## Continuous integration

The CI workflow runs on every push to `main` and on pull requests. It runs the tests and then `release.ps1` with a placeholder version, so a problem in the release build shows up before a tag is pushed. The file it builds is attached to the run as an artifact.
