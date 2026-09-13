# Cleanup presentation regression check (ASCII only on purpose).
#
# Loads the REAL compiled BAML, renders the real cleanup risk-section DataTemplate
# with synthetic data, measures the header at 860 DIP in both ZH and EN, and writes
# an isolated test-window screenshot. It never scans a disk, deletes anything, or
# sends a model request.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/CleanupPresentationCheck/Run.ps1

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$proj = Join-Path $repo 'tools\CleanupPresentationCheck\CleanupPresentationCheck.csproj'

# Prefer the SDK on PATH, but fall back to the per-user install used on this box.
$dotnet = 'dotnet'
$null = & $dotnet --list-sdks 2>$null
if ($LASTEXITCODE -ne 0) {
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $userDotnet) { $dotnet = $userDotnet }
}

Write-Output 'Building CleanupPresentationCheck (Release)...'
$build = & $dotnet build $proj -c Release --nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Output $build
    Write-Output 'FAIL: CleanupPresentationCheck did not build.'
    exit 1
}

$dll = Join-Path $repo 'tools\CleanupPresentationCheck\bin\Release\net8.0-windows10.0.18362.0\CleanupPresentationCheck.dll'
if (-not (Test-Path $dll)) {
    Write-Output "FAIL: built output missing: $dll"
    exit 1
}

& $dotnet $dll
exit $LASTEXITCODE
