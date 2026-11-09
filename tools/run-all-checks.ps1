# Runs every offline check and prints one PASS/FAIL summary.
#
# Why this exists: tools/release.ps1 says "Run those first and treat them as the
# release gate" but nothing ran them, so the gate was a convention people had to
# remember. This script makes it one command.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-all-checks.ps1
# Options: -Only <name substring>   -SkipBuild   -Quiet
#
# Exit code: 0 = all green, 1 = at least one failed, 2 = the suite itself broke.
# Full output of each check is written to $env:TEMP\dsh-checks\<name>.log.

param(
    [string]$Only = '',
    [switch]$SkipBuild,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logDir = Join-Path $env:TEMP 'dsh-checks'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Prefer an SDK-capable dotnet (PATH may resolve to a runtime-only install).
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

# ps1 = the check owns its own build/run (it is a script, not a project).
# exe = a plain console project we build and run here.
$checks = @(
    [pscustomobject]@{ Name = 'CleanAnalyzerCheck'; Kind = 'ps1' },
    [pscustomobject]@{ Name = 'SafetyCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'StartupCheck'; Kind = 'ps1' },
    [pscustomobject]@{ Name = 'UiRegressionCheck'; Kind = 'ps1' },
    [pscustomobject]@{ Name = 'CleanupPresentationCheck'; Kind = 'ps1' },
    [pscustomobject]@{ Name = 'AppPurposeCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'AppRecommendationCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'LocalRecognitionCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'RecognitionPersistenceCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'FolderDeleteCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'AgentToolCheck'; Kind = 'exe' },
    [pscustomobject]@{ Name = 'AgentReportCheck'; Kind = 'exe' }
) | Where-Object { $Only -eq '' -or $_.Name -like "*$Only*" }

if ($checks.Count -eq 0) { throw "No check matches -Only '$Only'." }

if (-not $SkipBuild) {
    Write-Host '== building every check project (Release) ==' -ForegroundColor Cyan
    foreach ($c in ($checks | Where-Object { $_.Kind -eq 'exe' })) {
        $proj = Join-Path $repo "tools\$($c.Name)\$($c.Name).csproj"
        if (-not (Test-Path -LiteralPath $proj)) { throw "missing project: $proj" }
        $prev = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $build = & $dotnet build $proj -c Release --nologo -v quiet 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prev
        if ($code -ne 0) {
            $build | Set-Content -LiteralPath (Join-Path $logDir "$($c.Name).build.log") -Encoding UTF8
            Write-Host "BUILD FAILED: $($c.Name)" -ForegroundColor Red
            $build | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" }
            exit 2
        }
    }
    Write-Host '   all check projects built' -ForegroundColor DarkGray
}

$results = @()
foreach ($c in $checks) {
    $log = Join-Path $logDir "$($c.Name).log"
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($c.Kind -eq 'ps1') {
        $script = Join-Path $repo "tools\$($c.Name)\Run.ps1"
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $script 2>&1
    }
    else {
        $proj = Join-Path $repo "tools\$($c.Name)\$($c.Name).csproj"
        $out = & $dotnet run -c Release --project $proj --no-build --nologo -v quiet 2>&1
    }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    $out | Set-Content -LiteralPath $log -Encoding UTF8

    # Checks print one "N / M" style summary line; keep it for the report.
    $summary = ($out | Select-String -Pattern 'PASS|FAIL|通过|失败' | Select-Object -Last 1)
    $results += [pscustomobject]@{
        Name    = $c.Name
        Ok      = ($code -eq 0)
        Code    = $code
        Summary = if ($summary) { $summary.Line.Trim() } else { '' }
    }
    if (-not $Quiet) {
        $tail = $out | Select-Object -Last 2
        $color = if ($code -eq 0) { 'Green' } else { 'Red' }
        Write-Host ("{0,-28} {1}" -f $c.Name, $(if ($code -eq 0) { 'PASS' } else { "FAIL (exit $code)" })) -ForegroundColor $color
        foreach ($line in $tail) { Write-Host "    $line" -ForegroundColor DarkGray }
    }
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host "FAIL: $($failed.Count) of $($results.Count) check(s) failed" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  - $($_.Name) (see $logDir\$($_.Name).log)" -ForegroundColor Red }
    exit 1
}
Write-Host "PASS: all $($results.Count) offline checks green." -ForegroundColor Green
if (-not $Quiet) {
    Write-Host 'Summary lines:' -ForegroundColor DarkGray
    $results | ForEach-Object { Write-Host ("  {0,-28} {1}" -f $_.Name, $_.Summary) -ForegroundColor DarkGray }
}
Write-Host "Full logs: $logDir" -ForegroundColor DarkGray
exit 0
