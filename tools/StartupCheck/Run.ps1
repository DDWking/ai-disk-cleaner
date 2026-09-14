# Startup regression check (ASCII only on purpose; keep the UTF-8 BOM if you edit this file).
#
# Why this exists: on 2026-09-13 the 2.8.0 build shipped with
#   MainWindow.xaml: RowHeight="Auto" MinRowHeight="46"
# DataGrid.RowHeight is a plain double with NO TypeConverter, so "Auto" throws a
# XamlParseException while the compiled BAML loads. The project still COMPILED and every
# string-level assertion in UiRegressionCheck still passed - but the window never appeared
# ("double-click does nothing"), and the half-initialised window sent the crash handler
# into a silent infinite loop.
#
# So this check loads the REAL compiled BAML (exactly what StartupUri does) and also
# measures the real control tree for the detail row. String assertions cannot catch this.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/StartupCheck/Run.ps1

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$proj = Join-Path $repo 'tools\StartupCheck\StartupCheck.csproj'

# Prefer an SDK-capable dotnet. PATH may resolve to a runtime-only install (no SDK).
#
# NOTE (PS 5.1): with $ErrorActionPreference = 'Stop', redirecting a native command's
# stderr (`2>$null`) raises NativeCommandError and kills the script - which made this
# check look broken on a machine whose PATH dotnet is runtime-only. So: temporarily
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

Write-Output 'Building StartupCheck (Release)...'
$build = & $dotnet build $proj -c Release --nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Output $build
    Write-Output 'FAIL: StartupCheck did not build.'
    exit 1
}

$dll = Join-Path $repo 'tools\StartupCheck\bin\Release\net8.0-windows10.0.18362.0\StartupCheck.dll'
if (-not (Test-Path $dll)) {
    Write-Output "FAIL: built output missing: $dll"
    exit 1
}

& $dotnet $dll
exit $LASTEXITCODE
