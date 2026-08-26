<#
.SYNOPSIS
  Bisect groundwater_u_factor on US-native Ex4 deck until fem_cpp FoS ~ target (default 1.27).

.EXAMPLE
  powershell -File tools/calibrate_ex4_u_factor.ps1 -Rebuild
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $Rebuild,
    [double] $TargetFoS = 1.27,
    [double] $FoSTol = 0.02,
    [double] $AlphaLo = 0.0,
    [double] $AlphaHi = 1.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}

. (Join-Path $RepoRoot 'tools\SrfResParse.ps1')

$verifyDir = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope_groundwater'
$baseName = 'ex4_us_native'
$datPath = Join-Path $verifyDir "$baseName.dat"
$resPath = Join-Path $verifyDir "$baseName.res"
$femCpp = Join-Path $RepoRoot 'build\Release\fem_cpp.exe'
$genScript = Join-Path $RepoRoot 'tools\gen_slope64_ex4_groundwater.ps1'

function Invoke-Ex4Run {
    param([double] $Alpha, [string] $Ladder = 'smith')
    & powershell -ExecutionPolicy Bypass -File $genScript `
        -OutPath $datPath -Units us -CoordSmithNative -SrfLadder $Ladder `
        -GroundwaterStrength effective -GroundwaterUFactor $Alpha | Out-Null
    Push-Location $verifyDir
    try {
        & $femCpp $baseName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "fem_cpp failed alpha=$Alpha" }
    } finally { Pop-Location }
    $s = Get-SrfResSummary -ResPath $resPath
    $fos = Get-FoSFromTrials $s.Trials $s.Limit
    return @{ FoS = $fos; Summary = $s }
}

if ($Rebuild) {
    cmake --build (Join-Path $RepoRoot 'build') --config Release --target fem_cpp | Out-Host
}
if (-not (Test-Path $femCpp)) { throw "Build fem_cpp first: $femCpp" }

Write-Host "=== Calibrate groundwater_u_factor (US native) ==="
Write-Host "Target FoS = $TargetFoS +/- $FoSTol"
Write-Host ""

$lo = Invoke-Ex4Run -Alpha $AlphaLo -Ladder smith
$hi = Invoke-Ex4Run -Alpha $AlphaHi -Ladder smith
Write-Host ("alpha={0:G}: FoS={1}" -f $AlphaLo, $(if ($lo.FoS) { "{0:F4}" -f $lo.FoS } else { 'all conv (>smith max)' }))
Write-Host ("alpha={0:G}: FoS={1}" -f $AlphaHi, $(if ($hi.FoS) { "{0:F4}" -f $hi.FoS } else { 'all conv' }))

$bestAlpha = 0.55
$bestFoS = $null
$bestErr = [double]::MaxValue

for ($iter = 0; $iter -lt 14; $iter++) {
    $mid = 0.5 * ($AlphaLo + $AlphaHi)
    $midRun = Invoke-Ex4Run -Alpha $mid -Ladder smith
    $fos = $midRun.FoS
    if (-not $fos) {
        Write-Host "alpha=$mid : all converged on smith ladder; increase alpha"
        $AlphaLo = $mid
        continue
    }
    $err = [math]::Abs($fos - $TargetFoS)
    Write-Host ("iter {0}: alpha={1:G6} FoS={2:F4} err={3:F4}" -f $iter, $mid, $fos, $err)
    if ($err -lt $bestErr) { $bestErr = $err; $bestAlpha = $mid; $bestFoS = $fos }
    if ($err -le $FoSTol) { break }

    if ($fos -gt $TargetFoS) { $AlphaLo = $mid } else { $AlphaHi = $mid }
}

Write-Host ""
Write-Host ("Best: groundwater_u_factor={0:G6}  FoS={1:F4}  err={2:F4}" -f $bestAlpha, $bestFoS, $bestErr)

# Validate on book4 ladder
$book = Invoke-Ex4Run -Alpha $bestAlpha -Ladder book4
Write-Host ("Book4 ladder FoS={0}" -f $(if ($book.FoS) { "{0:F4}" -f $book.FoS } else { 'n/a' }))

$metricOut = Join-Path $verifyDir 'ex4_homogeneous_slope_groundwater.dat'
& powershell -ExecutionPolicy Bypass -File $genScript `
    -OutPath $metricOut -Units metric -SrfLadder book4 `
    -GroundwaterStrength effective -GroundwaterUFactor $bestAlpha | Out-Host

Write-Host "Wrote calibrated metric deck: $metricOut"
exit $(if ($bestErr -le $FoSTol) { 0 } else { 1 })
