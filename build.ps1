# 编译番剧批量重命名（单文件 EXE）
# 依赖：Windows 自带的 .NET Framework 4.x 编译器（csc.exe），无需安装 Python / Node。
param(
    [switch]$Test
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "找不到 .NET Framework 编译器 csc.exe"
}

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = @(Get-ChildItem (Join-Path $root "src\*.cs") | ForEach-Object { $_.FullName })
$refs = @(
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll",
    "/r:System.Web.Extensions.dll"
)

$icon = Join-Path $root "src\app.ico"
$manifest = Join-Path $root "src\app.manifest"
$outExe = Join-Path $dist "AnimeRenamer.exe"

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    ("/win32icon:" + $icon),
    ("/win32manifest:" + $manifest),
    ("/out:" + $outExe)
)
$cscArgs += $refs
$cscArgs += $sources

Write-Host "编译 AnimeRenamer.exe ..."
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

Write-Host ("完成: {0}  ({1:N0} KB)" -f $outExe, ((Get-Item $outExe).Length / 1KB))

if ($Test) {
    $testDir = Join-Path $root "build\testdata"
    New-Item -ItemType Directory -Force -Path $testDir | Out-Null
    cmd /c start "" /wait $outExe --selftest --data-dir $testDir
    Get-Content (Join-Path $testDir "selftest-result.txt") -Encoding UTF8 | Select-Object -Last 4
}
