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

Write-Output 'Building StartupCheck (Release)...'
$build = & dotnet build $proj -c Release --nologo -v quiet 2>&1
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

& dotnet $dll
exit $LASTEXITCODE
