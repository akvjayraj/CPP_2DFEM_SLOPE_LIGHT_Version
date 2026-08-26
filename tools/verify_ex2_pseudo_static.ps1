# Smoke/regression: Ex2 two-layer slope baseline vs seismic pseudo-static deck.
# Asserts .res logs k_h/k_v and JSON exports pseudo_static_nodal_forces when coeffs are non-zero.
param(
    [string]$FemCpp = "",
    [switch]$SkipBaseline
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if ([string]::IsNullOrWhiteSpace($FemCpp)) {
    $candidates = @(
        Join-Path $root "build\Release\fem_cpp.exe",
        Join-Path $root "library\solver\fem_cpp.exe",
        Join-Path $root "X2D_GUI\bin\Release\net8.0-windows\fem_cpp.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $FemCpp = $c; break }
    }
}
if (-not (Test-Path $FemCpp)) {
    throw "fem_cpp.exe not found. Build with: cmake --build build --config Release --target fem_cpp"
}

function Invoke-P64Run {
    param([string]$DatPath)
    $dir = Split-Path -Parent $DatPath
    $name = [System.IO.Path]::GetFileNameWithoutExtension($DatPath)
    Push-Location $dir
    try {
        & $FemCpp $name
        if ($LASTEXITCODE -ne 0) { throw "fem_cpp exit $LASTEXITCODE for $name" }
    } finally {
        Pop-Location
    }
    return @{
        Res  = Join-Path $dir "$name.res"
        Json = Join-Path $dir "$name.json"
    }
}

function Get-MaxDispFromRes {
    param([string]$ResPath)
    $max = 0.0
    foreach ($line in Get-Content $ResPath) {
        if ($line -match '^\s*[\d.]+\s+([\d.Ee+\-]+)\s+\d+\s*$') {
            $v = [double]$Matches[1]
            if ($v -gt $max) { $max = $v }
        }
    }
    return $max
}

$baseline = Join-Path $root "Verification\slope\Ex2_twolayerslope\ex2_twolayer_slope.dat"
$seismic  = Join-Path $root "Verification\slope\Ex2_twolayerslope_seismic\ex2_twolayer_slope_seismic.dat"

if (-not (Test-Path $seismic)) { throw "Missing seismic deck: $seismic" }

$baseMax = $null
if (-not $SkipBaseline) {
    if (-not (Test-Path $baseline)) { throw "Missing baseline deck: $baseline" }
    Write-Host "Running baseline Ex2..."
    $b = Invoke-P64Run $baseline
    $baseMax = Get-MaxDispFromRes $b.Res
    Write-Host "  baseline max|u| (from .res) = $baseMax"
}

Write-Host "Running Ex2 seismic (k_h=0.1, k_v=0.05)..."
$s = Invoke-P64Run $seismic

$resText = Get-Content $s.Res -Raw
if ($resText -notmatch 'Pseudo-static coefficients:\s*k_h=0\.1\s+k_v=0\.05') {
    throw ".res missing expected pseudo-static coefficient line"
}
Write-Host "  OK: .res contains k_h/k_v line"

$json = Get-Content $s.Json -Raw | ConvertFrom-Json
if (-not $json.pseudo_static) { throw "JSON missing pseudo_static block" }
if ([math]::Abs($json.pseudo_static.k_h - 0.1) -gt 1e-6) { throw "JSON k_h mismatch" }
if ([math]::Abs($json.pseudo_static.k_v - 0.05) -gt 1e-6) { throw "JSON k_v mismatch" }
if (-not $json.pseudo_static_nodal_forces -or $json.pseudo_static_nodal_forces.Count -lt 1) {
    throw "JSON missing pseudo_static_nodal_forces array"
}
$fmax = ($json.pseudo_static_nodal_forces | ForEach-Object {
    [math]::Sqrt($_.fx * $_.fx + $_.fy * $_.fy)
} | Measure-Object -Maximum).Maximum
Write-Host "  OK: JSON pseudo_static + $($json.pseudo_static_nodal_forces.Count) nodal forces (|F|_max=$fmax)"

$seisMax = Get-MaxDispFromRes $s.Res
Write-Host "  seismic max|u| (from .res) = $seisMax"

if ($null -ne $baseMax -and $seisMax -le $baseMax * 0.99) {
    Write-Warning "Expected seismic max|u| > baseline (got seismic=$seisMax, baseline=$baseMax). Check mesh/SRF convergence."
}

Write-Host "verify_ex2_pseudo_static: PASS"
