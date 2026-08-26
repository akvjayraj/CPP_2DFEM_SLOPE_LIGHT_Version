<#
.SYNOPSIS
  Compare fem_cpp Example 4 .res against SLOPE64 benchmarks/slope64_ref/ex4.res.

.EXAMPLE
  powershell -File tools/compare_slope64_ex4.ps1
  powershell -File tools/compare_slope64_ex4.ps1 -FemRes Verification/slope/Ex2_twolayerslope_groundwater/ex4_us_native.res
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $RefRes,
    [string] $FemRes,
    [double] $FoSTol = 0.04,
    [double] $DispTolPct = 15.0,
    [double] $FemDispScale = 1.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}

. (Join-Path $RepoRoot 'tools\SrfResParse.ps1')

if (-not $RefRes) {
    $RefRes = Join-Path $RepoRoot 'benchmarks\slope64_ref\ex4.res'
}
if (-not $FemRes) {
    $FemRes = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope_groundwater\ex4_homogeneous_slope_groundwater.res'
}

$ref = Get-SrfResSummary -ResPath $RefRes
$fem = Get-SrfResSummary -ResPath $FemRes

$refFoS = if ($ref.FoS) { $ref.FoS } else { Get-FoSFromTrials $ref.Trials $ref.Limit }
$femFoS = Get-FoSFromTrials $fem.Trials $fem.Limit

Write-Host "=== Example 4: fem_cpp vs SLOPE64 ==="
Write-Host "Reference: $RefRes"
Write-Host "fem_cpp:   $FemRes"
Write-Host ""
Write-Host ("SLOPE64 FoS: {0}" -f $(if ($refFoS) { "{0:F4}" -f $refFoS } else { 'n/a' }))
Write-Host ("fem_cpp FoS: {0}" -f $(if ($femFoS) { "{0:F4}" -f $femFoS } else { 'no bracket' }))
Write-Host ""

Write-Host ("{0,8}  {1,12}  {2,5}  |  {3,12}  {4,5}  {5,8}" -f 'SRF', 'ref|u|', 'iters', 'fem|u|', 'iters', 'd%')
foreach ($rt in $ref.Trials) {
    $ft = $fem.Trials | Where-Object { [math]::Abs($_.Srf - $rt.Srf) -lt 0.0015 } | Select-Object -First 1
    if ($ft) {
        $dPct = 100.0 * [math]::Abs($ft.MaxDisp * $FemDispScale - $rt.MaxDisp) / [math]::Max($rt.MaxDisp, 1e-12)
        Write-Host ("{0,8:F4}  {1,12:E4}  {2,5}  |  {3,12:E4}  {4,5}  {5,7:F1}%" -f `
            $rt.Srf, $rt.MaxDisp, $rt.Iters, $ft.MaxDisp, $ft.Iters, $dPct)
    } else {
        Write-Host ("{0,8:F4}  {1,12:E4}  {2,5}  |  {3}" -f $rt.Srf, $rt.MaxDisp, $rt.Iters, '(missing)')
    }
}

$passFoS = $femFoS -and $refFoS -and ([math]::Abs($femFoS - $refFoS) -le $FoSTol)
Write-Host ""
Write-Host ("FoS delta: {0:F4}  (tol {1:F2})  {2}" -f `
    $(if ($femFoS -and $refFoS) { $femFoS - $refFoS } else { [double]::NaN }), $FoSTol, $(if ($passFoS) { 'PASS' } else { 'FAIL' }))

exit $(if ($passFoS) { 0 } else { 1 })
