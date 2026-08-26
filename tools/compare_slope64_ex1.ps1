<#
.SYNOPSIS
  Compare p610 T3 SRF displacements to SLOPE64 reference (ex1.vec arrows + ex1.res FoS).

.EXAMPLE
  powershell -File tools/compare_slope64_ex1.ps1 `
    -JsonPath scripts/slope_stability_T3_SRF/ex1_homogeneous_slope.json `
    -RefDir benchmarks/slope64_ref
#>
[CmdletBinding()]
param(
    [string] $JsonPath,
    [string] $RefDir,
    [string] $RepoRoot,
    [double] $MatchTol = 0.05
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
if (-not $JsonPath) {
    $JsonPath = Join-Path $RepoRoot 'scripts\slope_stability_T3_SRF\ex1_homogeneous_slope.json'
}
if (-not $RefDir) {
    $RefDir = Join-Path $RepoRoot 'benchmarks\slope64_ref'
}

$vecPath = Join-Path $RefDir 'ex1.vec'
$resPath = Join-Path $RefDir 'ex1.res'
if (-not (Test-Path $JsonPath)) { throw "Missing JSON: $JsonPath" }
if (-not (Test-Path $vecPath)) { throw "Missing reference: $vecPath (run SLOPE64 ex1 or copy from Mines distribution)" }

# --- SLOPE64 FoS from ex1.res ---
$slope64Fos = $null
if (Test-Path $resPath) {
    $m = Select-String -Path $resPath -Pattern 'Estimated Factor of Safety\s*=\s*([\d.]+)' | Select-Object -First 1
    if ($m) { $slope64Fos = [double]$m.Matches[0].Groups[1].Value }
}

# --- Parse ex1.vec PostScript arrows (from_x from_y to_x to_y) ---
$arrowLines = Select-String -Path $vecPath -Pattern '^\s*[\d.]+\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+arrow\s*$'
$arrows = foreach ($line in $arrowLines) {
    $p = ($line.Line.Trim() -replace '\s+arrow\s*$','') -split '\s+'
    $fx = [double]$p[0]; $fy = [double]$p[1]; $tx = [double]$p[2]; $ty = [double]$p[3]
    $dx = $tx - $fx; $dy = $ty - $fy
    $mag = [Math]::Sqrt($dx*$dx + $dy*$dy)
    if ($mag -lt 1e-12) { continue }
    [pscustomobject]@{ dx = $dx/$mag; dy = $dy/$mag; mag = $mag }
}

# --- Our JSON nodal displacements ---
$j = Get-Content $JsonPath -Raw | ConvertFrom-Json
$ours = foreach ($n in $j.nodes) {
    $mag = [Math]::Sqrt($n.ux*$n.ux + $n.uy*$n.uy)
    if ($mag -lt 1e-14) { continue }
    [pscustomobject]@{
        x = [double]$n.x; y = [double]$n.y
        ux = [double]$n.ux; uy = [double]$n.uy
        mag = $mag
        dx = $n.ux / $mag; dy = $n.uy / $mag
    }
}

$maxOurs = ($ours | Measure-Object -Property mag -Maximum).Maximum
$maxRef = ($arrows | Measure-Object -Property mag -Maximum).Maximum

# Direction correlation: bucket our nodes onto a coarse grid, compare dominant arrow dirs
$bins = @{}
foreach ($o in $ours) {
    $bx = [int][Math]::Floor($o.x / 2.0)
    $by = [int][Math]::Floor($o.y / 2.0)
    $key = "$bx,$by"
    if (-not $bins.ContainsKey($key) -or $o.mag -gt $bins[$key].mag) {
        $bins[$key] = $o
    }
}

$refIdx = 0
$dotSum = 0.0
$paired = 0
foreach ($a in $arrows) {
    if ($refIdx -ge $bins.Count) { break }
    $keys = @($bins.Keys)
    $o = $bins[$keys[$refIdx % $keys.Count]]
    $dot = $o.dx * $a.dx + $o.dy * $a.dy
    $dotSum += [Math]::Abs($dot)
    $paired++
    $refIdx++
}

$meanAlign = if ($paired -gt 0) { $dotSum / $paired } else { 0 }

Write-Host "=== SLOPE64 Example 1 displacement comparison ==="
if ($slope64Fos) { Write-Host ("SLOPE64 estimated FoS (Q8): {0:F3}" -f $slope64Fos) }
if ($j.meta.srf) {
    Write-Host ("p610 trial srf_F: {0:F3}  converged: {1}  max|u|: {2:F5} m" -f `
        $j.meta.srf.F_trial, $j.meta.srf.all_increments_converged, $j.meta.srf.max_disp)
}
Write-Host ("Our peak |u|: {0:F5} m  (SLOPE64 near-failure ~0.036 m)" -f $maxOurs)
Write-Host ("Reference arrows: {0}  Our displaced nodes: {1}" -f $arrows.Count, $ours.Count)
Write-Host ("Coarse direction alignment score (0-1, higher=better): {0:F3}" -f $meanAlign)
Write-Host ""
Write-Host "Note: ex1.vec uses PostScript display coordinates; use for mechanism shape."
Write-Host "Regenerate .dat with: tools/gen_slope64_ex1.ps1 -SrfF 1.547"
