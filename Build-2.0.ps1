#requires -version 5.1
<#
.SYNOPSIS
    Falco GUI 一键构建：发布（自包含 R2R）→ 打包安装程序
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Build-2.0.ps1 -Version 2.0.0
#>
param(
    [string]$Version = '2.0.0',
    [switch]$SkipInstall   # 只发布不打包安装程序
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath

# 1) 版本号写入 csproj
$csproj = Join-Path $root 'src\Falco.App\Falco.App.csproj'
( Get-Content $csproj -Raw -Encoding UTF8 ) `
    -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" |
    Set-Content $csproj -Encoding UTF8 -NoNewline
Write-Host "[1/3] 版本号 → $Version" -ForegroundColor Cyan

# 2) 发布（自包含 + ReadyToRun，约 168MB 目录）
$rel = Join-Path $root 'src\Falco.App\release'
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
dotnet publish (Join-Path $root 'src\Falco.App\Falco.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -v q -o $rel
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }
Write-Host "[2/3] 发布完成 → $rel" -ForegroundColor Cyan

if ($SkipInstall) { return }

# 3) 打包安装程序（Inno Setup）
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
$iss = Join-Path $root 'Falco-Setup-2.0.iss'
( Get-Content $iss -Raw -Encoding UTF8 ) `
    -replace '#define MyAppVersion "[^"]*"', "#define MyAppVersion `"$Version`"" |
    Set-Content $iss -Encoding UTF8 -NoNewline
& $iscc $iss | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 编译失败' }
$out = Join-Path $root "dist\Falco-Setup-$Version.exe"
$msg = "[3/3] 安装包 → {0}（{1:N0} MB）" -f $out, ((Get-Item $out).Length / 1MB)
Write-Host $msg -ForegroundColor Cyan

# 可选：签名（证书就绪后取消注释）
# signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /n "<证书名>" $out
