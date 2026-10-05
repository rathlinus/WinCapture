<#
.SYNOPSIS
    Builds WinCapture: the capture program and the editor.

.DESCRIPTION
    Runs the tests, then publishes the capture program with Native AOT to
    artifacts\app and the editor to artifacts\app\Editor. Neither needs .NET
    or anything else installed.

.PARAMETER SkipTests
    Publish without running the tests first.

.PARAMETER Run
    Start the freshly built WinCapture afterwards, replacing a running one.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,

    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Output = Join-Path $RepoRoot 'artifacts\app'

# Runs a native command and fails the script when it fails.
function Invoke-Native([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Command)) $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipTests) {
    Write-Host 'Running the tests...'
    Invoke-Native dotnet @('test', (Join-Path $RepoRoot 'tests\WinCapture.Core.Tests'), '--nologo', '-v:q')
}

# A running WinCapture or editor keeps its files locked.
$running = Get-Process WinCapture, WinCapture.Editor -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Stopping the running WinCapture...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# The Native AOT targets run Visual Studio's vcvarsall.bat, which calls a bare
# "vswhere.exe". When that is not on PATH its error text ends up in the linker
# path and the publish fails, so make it resolvable.
$env:PATH = "$env:PATH;$(Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer')"

Write-Host 'Publishing WinCapture (Native AOT)...'
Invoke-Native dotnet @(
    'publish', (Join-Path $RepoRoot 'src\WinCapture.App\WinCapture.App.csproj'),
    '-c', 'Release', '-o', $Output, '--nologo', '-v:q')

# The editor goes into a folder of its own: as a WinUI program it brings .NET
# and the Windows App SDK with it, a few hundred files.
Write-Host 'Publishing the editor (WinUI)...'
$editorOutput = Join-Path $Output 'Editor'
if (Test-Path $editorOutput) {
    # Publishing does not remove files an earlier build left there.
    Remove-Item $editorOutput -Recurse -Force
}

Invoke-Native dotnet @(
    'publish', (Join-Path $RepoRoot 'src\WinCapture.Editor\WinCapture.Editor.csproj'),
    '-c', 'Release', '-p:Platform=x64', '-o', $editorOutput, '--nologo', '-v:q')

$exe = Join-Path $Output 'WinCapture.exe'
Write-Host "Built: $exe"

if ($Run) {
    Start-Process $exe
    Write-Host 'WinCapture is running. Press Print Screen to capture.'
}
