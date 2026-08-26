<#
.SYNOPSIS
  Run Example 4 groundwater verification vs SLOPE64 ex4.res golden reference.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/run_ex4_groundwater_verify.ps1
  powershell -ExecutionPolicy Bypass -File tools/run_ex4_groundwater_verify.ps1 -Rebuild
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $Rebuild,
    [double] $BookFoS = 1.27,
    [double] $FosTol = 0.04
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}

$verifyDir = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope_groundwater'
$baseName = 'ex4_homogeneous_slope_groundwater'
$datPath = Join-Path $verifyDir "$baseName.dat"
$resPath = Join-Path $verifyDir "$baseName.res"
$femCpp = Join-Path $RepoRoot 'build\Release\fem_cpp.exe'
$refRes = Join-Path $RepoRoot 'benchmarks\slope64_ref\ex4.res'

Write-Host "Regenerating Example 4 deck (book4 ladder, effective stress u_factor=0.55)..."
& powershell -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'tools\gen_slope64_ex4_groundwater.ps1') `
    -SrfLadder book4 -GroundwaterStrength effective -GroundwaterUFactor 0.55 | Out-Host

if ($Rebuild) {
    Write-Host "Rebuilding fem_cpp (Release)..."
    cmake --build (Join-Path $RepoRoot 'build') --config Release --target fem_cpp | Out-Host
}

if (-not (Test-Path $femCpp)) {
    throw "fem_cpp not found at $femCpp. Build with: cmake --build build --config Release --target fem_cpp"
}

Write-Host "Running fem_cpp on $baseName ..."
Push-Location $verifyDir
try {
    & $femCpp $baseName
    if ($LASTEXITCODE -ne 0) { throw "fem_cpp exited with code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

if (-not (Test-Path $resPath)) {
    throw "Result file not created: $resPath"
}

Write-Host ""
Write-Host "=== Compare fem_cpp vs SLOPE64 ex4.res ==="
& powershell -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'tools\compare_slope64_ex4.ps1') `
    -RefRes $refRes -FemRes $resPath -FoSTol $FosTol
