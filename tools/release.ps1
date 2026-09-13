# Release packaging gate: build -> self-contained publish -> zip -> SHA256 -> hash check.
#
# Reads <Version> from AiDiskCleaner.csproj (never hard-codes it), so it stays
# correct across version bumps. Output goes to dist/DashaoHuo-<ver>-<yyyyMMdd>-win-x64
# by default, alongside a .zip and .zip.sha256.
#
# The DLL hash check answers "did we ship exactly the build we verified": the
# published AiDiskCleaner.dll must be byte-identical (SHA256) to the Release
# win-x64 build output.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/release.ps1
# Options: -OutDir <path>  -SkipBuild  -SkipPublish  -SkipZip  -SkipHash
#
# NOTE: this does NOT run the offline check suite (StartupCheck / SafetyCheck /
# UiRegressionCheck / CleanupPresentationCheck / CleanAnalyzerCheck /
# AiNoteParserCheck / AppRecommendationCheck / LocalRecognitionCheck /
# ItemAiIsolationCheck). Run those first and treat them as the release gate.

param(
    [string]$OutDir = '',
    [switch]$SkipBuild = $false,
    [switch]$SkipPublish = $false,
    [switch]$SkipZip = $false,
    [switch]$SkipHash = $false
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# 1) locate an SDK-capable dotnet (PATH first, then the per-user install).
$dotnet = 'dotnet'
& $dotnet --list-sdks *> $null
if ($LASTEXITCODE -ne 0) {
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $userDotnet) { $dotnet = $userDotnet }
    else { throw "No .NET SDK found (neither dotnet on PATH nor $userDotnet)." }
}
Write-Output "dotnet: $dotnet"

$proj = Join-Path $repo 'src\AiDiskCleaner\AiDiskCleaner.csproj'
$csprojText = Get-Content -LiteralPath $proj -Raw -Encoding UTF8
$ver = [regex]::Match($csprojText, '<Version>([^<]+)</Version>').Groups[1].Value.Trim()
if (-not $ver) { throw 'Could not read <Version> from AiDiskCleaner.csproj.' }

$stamp = Get-Date -Format 'yyyyMMdd'
$distRoot = Join-Path $repo 'dist'
$out = if ($OutDir) { $OutDir } else { Join-Path $distRoot "DashaoHuo-$ver-$stamp-win-x64" }
Write-Output "version: $ver"
Write-Output "output : $out"

# 2) release build gate (0 warnings, 0 errors).
if (-not $SkipBuild) {
    Write-Output '=== dotnet build -c Release -r win-x64 ==='
    & $dotnet build $proj -c Release -r win-x64 --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

$buildDll = Join-Path $repo "src\AiDiskCleaner\bin\Release\net8.0-windows10.0.18362.0\win-x64\AiDiskCleaner.dll"

# 3) self-contained publish.
if (-not $SkipPublish) {
    if (Test-Path $out) { throw "Output already exists: $out (remove it or pick a new -OutDir)." }
    Write-Output '=== dotnet publish (self-contained win-x64) ==='
    & $dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}

# 4) sidecar reminder (optional by design: AI falls back to the built-in HTTP path).
$sidecar = Join-Path $out 'sidecar\AiSidecar.exe'
if (-not (Test-Path $sidecar)) {
    Write-Warning 'No sidecar\AiSidecar.exe in the package (AI uses the built-in fallback).'
}

# 5) hash check: shipped DLL must equal the acceptance-build DLL.
$pubDll = Join-Path $out 'AiDiskCleaner.dll'
$buildHash = (Get-FileHash -LiteralPath $buildDll -Algorithm SHA256).Hash
$pubHash = (Get-FileHash -LiteralPath $pubDll -Algorithm SHA256).Hash
if ($buildHash -ne $pubHash) {
    throw "DLL hash mismatch: build=$buildHash publish=$pubHash"
}
Write-Output "AiDiskCleaner.dll SHA256 = $buildHash  (build == publish)"

# 6) zip + sha256.
$zip = "$out.zip"
if (-not $SkipZip) {
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    Write-Output '=== zip ==='
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
}
if (-not $SkipHash) {
    if (-not (Test-Path $zip)) { throw "Zip missing: $zip" }
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    $shaFile = "$zip.sha256"
    Set-Content -LiteralPath $shaFile -Value "$zipHash  $(Split-Path $zip -Leaf)" -Encoding ascii
    Write-Output "zip SHA256 = $zipHash"
    Write-Output "wrote $shaFile"
}

Write-Output 'OK'
