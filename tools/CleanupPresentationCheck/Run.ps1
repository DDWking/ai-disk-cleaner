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

# Prefer an SDK-capable dotnet. PATH may resolve to a runtime-only install (no SDK).
#
# NOTE (PS 5.1): with $ErrorActionPreference = 'Stop', redirecting a native command's
# stderr (`2>$null`) raises NativeCommandError and kills the script. So: temporarily
# relax the preference, merge stderr with 2>&1, and decide by exit code + real output.
function Find-Dotnet {
    $candidates = @()
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath -and $onPath.Source) { $candidates += $onPath.Source }
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $userDotnet) { $candidates += $userDotnet }
    foreach ($candidate in $candidates) {
        $prev = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $sdks = (& $candidate --list-sdks 2>&1 | Out-String)
            $code = $LASTEXITCODE
        }
        catch { $sdks = ''; $code = 1 }
        finally { $ErrorActionPreference = $prev }
        if ($code -eq 0 -and $sdks -match '\d+\.\d+\.\d+') { return $candidate }
    }
    throw 'No .NET SDK found (neither dotnet on PATH nor ~/.dotnet/dotnet.exe has an SDK).'
}

$dotnet = Find-Dotnet
Write-Output "dotnet: $dotnet"

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
