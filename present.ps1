#requires -Version 7.0
<#
.SYNOPSIS
  Showcase presenter for the SkiaSharp + Uno demos.
  Launches each demo in showcase order (showcase-order.json). Close a window and the
  next one opens automatically. Uses RELEASE builds (clean, no dev overlay/logging).

.EXAMPLE
  ./present.ps1                # run the whole showcase in order
  ./present.ps1 -StartAt 5     # resume from #5
  ./present.ps1 -Config Debug  # use Debug builds instead
  ./present.ps1 -Loop          # loop back to the start after the last demo
#>
[CmdletBinding()]
param(
  [int]    $StartAt = 1,
  [ValidateSet('Release', 'Debug')] [string] $Config = 'Release',
  [switch] $Loop
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$order = Get-Content (Join-Path $root 'showcase-order.json') -Raw | ConvertFrom-Json
$total = $order.Count

function Exe($name) { Join-Path $root "demos/$name/$name/bin/$Config/net10.0-desktop/$name.exe" }
function Ensure($name) {
  if (-not (Test-Path (Exe $name))) {
    Write-Host "  building $name ($Config)..." -ForegroundColor DarkGray
    dotnet build (Join-Path $root "demos/$name/$name/$name.csproj") -c $Config | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }
  }
}

Write-Host ""
Write-Host "  Fun with SkiaSharp - showcase ($total demos, $Config)" -ForegroundColor Cyan
Write-Host "  Close a demo window to advance to the next. Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

for ($i = $StartAt - 1; $i -lt $total; $i++) {
  $d = $order[$i]
  Ensure $d.name
  $tag = if ($d.type -eq 'game') { '[GAME]' } else { '[demo]' }
  Write-Host ("  {0,2}/{1}  {2}  {3}" -f $d.rank, $total, $tag, $d.name) -ForegroundColor Yellow
  Write-Host ("        {0}" -f $d.oneLiner) -ForegroundColor Gray
  Write-Host ("        controls: {0}" -f $d.controls) -ForegroundColor DarkGray

  $p = Start-Process -FilePath (Exe $d.name) -PassThru
  $p.WaitForExit()
  Write-Host "        (closed)" -ForegroundColor DarkGray
  Write-Host ""
  Start-Sleep -Milliseconds 500

  if ($Loop -and $i -eq $total - 1) { $i = -1 }
}

Write-Host "  Showcase complete. Woo-hoo!" -ForegroundColor Green
