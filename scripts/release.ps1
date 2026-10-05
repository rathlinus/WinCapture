<#
.SYNOPSIS
    Builds what a GitHub release consists of.

.DESCRIPTION
    Builds WinCapture with the given version and packs it into

        artifacts\release\WinCapture-<version>-x64.zip

    which holds WinCapture.exe and the Editor folder. Unpacked anywhere, it
    runs without installing anything.

    The release workflow runs this for a version tag. It works the same on a
    developer machine, which is how to try a release before tagging one.

.PARAMETER Version
    The release version, such as 1.2.0.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [version]$Version
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $RepoRoot 'artifacts'

& (Join-Path $PSScriptRoot 'build.ps1') -Version $Version -SkipTests

$release = Join-Path $Artifacts 'release'
if (Test-Path $release) { Remove-Item $release -Recurse -Force }

# A copy without the debug symbols, which nobody running the program needs.
$shortVersion = "$($Version.Major).$($Version.Minor).$([Math]::Max($Version.Build, 0))"
$name = "WinCapture-$shortVersion-x64"
$bundle = Join-Path $release 'WinCapture'
New-Item -ItemType Directory -Force $release | Out-Null
Copy-Item (Join-Path $Artifacts 'app') $bundle -Recurse
Get-ChildItem $bundle -Recurse -Filter '*.pdb' | Remove-Item -Force

Write-Host 'Packing the release...'
Compress-Archive -Path $bundle -DestinationPath (Join-Path $release "$name.zip")
Remove-Item $bundle -Recurse -Force

Write-Host 'Release files:'
Get-ChildItem $release | ForEach-Object { Write-Host "  $($_.FullName)" }
