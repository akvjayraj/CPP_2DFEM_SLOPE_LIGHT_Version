<#
.SYNOPSIS
  Sweep groundwater_u_factor values on US-native Ex4 deck.
#>
param([string] $RepoRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..') }
. (Join-Path $RepoRoot 'tools\SrfResParse.ps1')

$verifyDir = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope_groundwater'
$femCpp = Join-Path $RepoRoot 'build\Release\fem_cpp.exe'
$alphas = @(0.50, 0.52, 0.54, 0.55, 0.56, 0.58)

Write-Host ("{0,8}  {1,8}  {2}" -f 'alpha', 'FoS', 'fail@SRF')
foreach ($a in $alphas) {
    $dat = Join-Path $verifyDir 'ex4_u_sweep.dat'
    $base = 'ex4_u_sweep'
    & powershell -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'tools\gen_slope64_ex4_groundwater.ps1') `
        -OutPath $dat -Units us -CoordSmithNative -SrfLadder book4 `
        -GroundwaterStrength effective -GroundwaterUFactor $a | Out-Null
    Push-Location $verifyDir
    & $femCpp $base | Out-Null
    Pop-Location
    $s = Get-SrfResSummary -ResPath (Join-Path $verifyDir "$base.res")
    $fos = Get-FoSFromTrials $s.Trials $s.Limit
    $failTrial = @($s.Trials | Where-Object { $_.Iters -ge $s.Limit } | Select-Object -First 1)
    $failSrf = if ($failTrial.Count -gt 0) { $failTrial[0].Srf } else { $null }
    Write-Host ("{0,8:G}  {1,8}  {2}" -f $a, $(if ($fos) { "{0:F4}" -f $fos } else { 'all conv' }), $(if ($failSrf) { "{0:F4}" -f $failSrf } else { '-' }))
}
