# 把 sidecar 打成自包含 exe（需要 bun）。
# 用法: powershell -ExecutionPolicy Bypass -File build-sidecar.ps1
# 产物: sidecar\AiSidecar.exe（86MB 左右，已 gitignore，不进版本库）
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sidecar = Join-Path $root 'sidecar'

# 找 bun（winget 装的不一定进了当前 PATH）
$bun = $null
foreach ($c in @(
  "$env:USERPROFILE\.bun\bin\bun.exe",
  "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Oven-sh.Bun_Microsoft.Winget.Source_8wekyb3d8bbwe\bun-windows-x64\bun.exe"
)) { if (Test-Path $c) { $bun = $c; break } }
if (-not $bun) { $bun = (Get-Command bun -ErrorAction SilentlyContinue)?.Source }
if (-not $bun) { throw '找不到 bun。装: winget install Oven-sh.Bun' }

Write-Host "bun: $bun"
Push-Location $sidecar
try {
  Write-Host '=== 安装依赖 ==='
  & cmd /c "npm install --ignore-scripts"
  if ($LASTEXITCODE -ne 0) { throw 'npm install 失败' }

  Write-Host '=== 编译自包含 exe ==='
  & $bun build --compile --target=bun-windows-x64-baseline ./src/index.js --outfile AiSidecar.exe
  if ($LASTEXITCODE -ne 0) { throw 'bun build 失败' }

  $size = [math]::Round((Get-Item AiSidecar.exe).Length / 1MB, 1)
  Write-Host "完成: $sidecar\AiSidecar.exe ($size MB)"
}
finally { Pop-Location }
