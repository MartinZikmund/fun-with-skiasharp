#requires -Version 7.0
<#
.SYNOPSIS
  Build & run the SkiaSharp + Uno Platform demos (Desktop / Win32).

.EXAMPLE
  ./run.ps1                 # lists all demos
  ./run.ps1 ShaderGalaxy    # build (if needed) + run that demo, maximized
  ./run.ps1 ShaderGalaxy -Rebuild
  ./run.ps1 -All -Thumbs    # regenerate every demo's thumbnail PNG (headless)
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0)] [string] $Demo,
  [switch] $Rebuild,
  [switch] $All,
  [switch] $Thumbs
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot 'demos'
$demos = Get-ChildItem $root -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name)/$($_.Name).csproj") } | Select-Object -ExpandProperty Name | Sort-Object

function Get-Proj($name) { Join-Path $root "$name/$name/$name.csproj" }
function Get-Exe($name)  { Join-Path $root "$name/$name/bin/Debug/net10.0-desktop/$name.exe" }

function Build($name) {
  Write-Host "Building $name ..." -ForegroundColor Cyan
  dotnet build (Get-Proj $name) -c Debug | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }
}

if ($All -and $Thumbs) {
  foreach ($d in $demos) {
    if ($Rebuild -or -not (Test-Path (Get-Exe $d))) { Build $d }
    $out = Join-Path $root "$d/thumb.png"
    & (Get-Exe $d) --thumb $out
    Write-Host "  thumb -> $out" -ForegroundColor Green
  }
  return
}

if (-not $Demo) {
  Write-Host "SkiaSharp + Uno Platform demos:`n" -ForegroundColor Yellow
  $demos | ForEach-Object { Write-Host "  - $_" }
  Write-Host "`nRun one with:  ./run.ps1 <DemoName>" -ForegroundColor DarkGray
  return
}

if ($Demo -notin $demos) { throw "Unknown demo '$Demo'. Available: $($demos -join ', ')" }

if ($Rebuild -or -not (Test-Path (Get-Exe $Demo))) { Build $Demo }

Write-Host "Launching $Demo ... (Alt+F4 or close the window to exit)" -ForegroundColor Green
Start-Process -FilePath (Get-Exe $Demo)
