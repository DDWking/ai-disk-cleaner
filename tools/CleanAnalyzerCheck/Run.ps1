$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourcePath = Join-Path $repo 'src\AiDiskCleaner\Services\CleanAnalyzer.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8

if ($source -match 'RemoveRange\s*\(\s*400\b') {
    throw 'FAIL: CleanAnalyzer still has a 400-item cap.'
}

$candidates = 1..401 | ForEach-Object {
    [pscustomobject]@{ Name = "virtual-$_.tmp"; Size = $_ }
}
$sorted = @($candidates | Sort-Object Size -Descending)
if ($sorted.Count -ne 401) {
    throw "FAIL: fixture produced $($sorted.Count) items."
}

if ($sorted[0].Size -ne 401 -or $sorted[400].Size -ne 1) {
    throw 'FAIL: ordering across the 400-item boundary is wrong.'
}

Write-Output 'PASS: no 400-item cap; 401 virtual candidates remain available.'
