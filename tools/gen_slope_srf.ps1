<#
.SYNOPSIS
  Generate Verification/geotech/slope_srf_taylor_phi0_t3/*.dat (vertical-cut
  cohesive slope, M2.2 Option B -- C++ strength-reduction-factor diagnostic).

.DESCRIPTION
  Mirrors gen_slope_taylor.ps1 (Option A lambda-sweep) but uses the new C++
  `! srf_F` analysis option introduced for M2.2 Option B. Each .dat in the
  folder freezes the same Taylor vertical-cut mesh and material; only the
  srf_F (strength-reduction factor) varies. The C++ solver scales every
  material's phi/c/psi by F before the in-situ + excavation solve and writes
  the resulting yield-ratio / plastic-strain / displacement diagnostics under
  meta.srf in the .json.

  The slope orchestrator (tools/build_all_slope_smoke_packages.ps1) collects
  the srf metadata from every variant and finds:

      srf_FoS_FE = smallest F at which max_plastic_strain > eps (= 1e-9)
                   AND max_yield_ratio >= 0.95

  i.e. the strength-reduction factor at which yield first saturates with
  plastic flow. This is a CONSERVATIVE lower-bound for the limit-equilibrium
  FoS in this small-strain viscoplastic solver -- the displacement field is
  geometrically restrained by the elastic backbone so the iteration loop
  itself never diverges, but the elastic-to-plastic transition point is
  cleanly visible in meta.srf and reproducible across runs.

  Header schema (consumed by VerificationViewModel and the orchestrator):
    ! expected_FoS                  <F>             # analytical FoS reference
    ! expected_FoS_tolerance_pct    <pct>           # default 50 % (soft signal)
    ! expected_FoS_mode             srf
    ! srf_F                         <F>             # the per-file reduction factor
    ! slope_geometry                <label>
    ! slope_angle_deg               <beta>
    ! slope_height_m                <H>

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/gen_slope_srf.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $scriptDir '..')
}

function Format-Double {
    param($Value)
    return ([double] $Value).ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
}

function New-SlopeSrfDat {
    param(
        [string] $OutPath,
        [double] $PhiDeg,
        [double] $C_kPa,
        [double] $GammaDesign_kNm3,
        [double] $SrfF,
        [double] $ExpectedFoS,
        [double] $ToleranceFoSPct,
        [double] $H_m       = 2.0,
        [double] $X_toe_m   = 3.0,
        [double] $L_m       = 8.0,
        [double] $D_m       = 4.0,
        [int]    $Nx        = 16,
        [int]    $Ny        = 8,
        [double] $E_kPa     = 1.0e4,
        [double] $Nu        = 0.3,
        [int]    $Incs      = 20,
        [int]    $Limit     = 250
    )

    if ($D_m -lt $H_m + 1e-9) { throw "D ($D_m) must be >= H ($H_m)" }

    $iToe = [int][math]::Round($Nx * ($X_toe_m / $L_m))
    if ($iToe -lt 1 -or $iToe -ge $Nx) { throw "X_toe out of range" }
    $jH = [int][math]::Round($Ny * (1.0 - $H_m / $D_m))
    if ($jH -lt 1 -or $jH -ge $Ny) { throw "H out of range" }

    $xs = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -le $Nx; $i++) {
        $xs.Add(($i / [double] $Nx) * $L_m) | Out-Null
    }
    $ys = New-Object System.Collections.Generic.List[double]
    for ($j = 0; $j -le $Ny; $j++) {
        $ys.Add(-((($Ny - $j) / [double] $Ny) * $D_m)) | Out-Null
    }

    $nodes   = New-Object System.Collections.Generic.List[object]
    $nodeIdx = @{}
    $idCounter = 1
    for ($j = 0; $j -le $Ny; $j++) {
        for ($i = 0; $i -le $Nx; $i++) {
            $nodes.Add([pscustomobject]@{ Id = $idCounter; X = $xs[$i]; Y = $ys[$j] }) | Out-Null
            $nodeIdx["$i,$j"] = $idCounter
            $idCounter++
        }
    }
    $nn = $nodes.Count

    $elems         = New-Object System.Collections.Generic.List[object]
    $excavateElems = New-Object System.Collections.Generic.List[int]
    $elId = 1
    for ($j = 0; $j -lt $Ny; $j++) {
        for ($i = 0; $i -lt $Nx; $i++) {
            $sw = $nodeIdx["$i,$j"]
            $se = $nodeIdx["$($i+1),$j"]
            $ne = $nodeIdx["$($i+1),$($j+1)"]
            $nw = $nodeIdx["$i,$($j+1)"]
            $isExcQuad = ($i -lt $iToe) -and ($j -ge $jH)
            $elems.Add([pscustomobject]@{ Id = $elId; N1 = $sw; N2 = $se; N3 = $ne }) | Out-Null
            if ($isExcQuad) { $excavateElems.Add($elId) | Out-Null }
            $elId++
            $elems.Add([pscustomobject]@{ Id = $elId; N1 = $sw; N2 = $ne; N3 = $nw }) | Out-Null
            if ($isExcQuad) { $excavateElems.Add($elId) | Out-Null }
            $elId++
        }
    }
    $nEls = $elems.Count
    $nExc = $excavateElems.Count

    $seen = @{}
    function Add-BC-Srf {
        param([int] $NodeId, [int] $FreeX, [int] $FreeY, [hashtable] $Map)
        if ($Map.ContainsKey($NodeId)) {
            $cur = $Map[$NodeId]
            $newFx = if ($FreeX -eq 0 -or $cur.Fx -eq 0) { 0 } else { 1 }
            $newFy = if ($FreeY -eq 0 -or $cur.Fy -eq 0) { 0 } else { 1 }
            $Map[$NodeId] = @{ Fx = $newFx; Fy = $newFy }
        } else {
            $Map[$NodeId] = @{ Fx = $FreeX; Fy = $FreeY }
        }
    }

    for ($i = 0; $i -le $Nx; $i++) {
        Add-BC-Srf -NodeId $nodeIdx["$i,0"] -FreeX 0 -FreeY 0 -Map $seen
    }
    for ($j = 0; $j -le $Ny; $j++) {
        Add-BC-Srf -NodeId $nodeIdx["0,$j"]   -FreeX 0 -FreeY 1 -Map $seen
        Add-BC-Srf -NodeId $nodeIdx["$Nx,$j"] -FreeX 0 -FreeY 1 -Map $seen
    }

    $bcLines = New-Object System.Collections.Generic.List[string]
    foreach ($nid in ($seen.Keys | Sort-Object {[int]$_})) {
        $fx = $seen[$nid].Fx
        $fy = $seen[$nid].Fy
        $bcLines.Add(("{0}  {1}  {2}" -f $nid, $fx, $fy)) | Out-Null
    }
    $nr = $bcLines.Count

    $crestNode = $nodeIdx["$iToe,$Ny"]
    $toeNode   = $nodeIdx["$iToe,$jH"]

    $sb = New-Object System.Text.StringBuilder
    $null = $sb.AppendLine("! <solver:p610_t3>")
    $null = $sb.AppendLine("! Taylor vertical-cut cohesive slope (T3, plane strain) - SRF F = $(Format-Double $SrfF)")
    $null = $sb.AppendLine("! M2.2 Option B: solver applies phi -> atan(tan phi / F), c -> c / F BEFORE solve.")
    $null = $sb.AppendLine("! Pre-excavation domain: [0, $L_m] m x [-$D_m, 0] m, $Nx x $Ny quads -> $nEls T3 elements ($nn nodes).")
    $null = $sb.AppendLine("! Excavation (layer 1): wedge x in [0, $X_toe_m] m, y in [-$H_m, 0] m -> $nExc removed.")
    $null = $sb.AppendLine("! Material design: phi=$PhiDeg deg, c=$C_kPa kPa, gamma=$GammaDesign_kNm3 kN/m^3, E=$E_kPa kPa, nu=$Nu.")
    $null = $sb.AppendLine("! Closed-form planar-slip FoS at F=1: 4*c/(gamma*H) = $((4*$C_kPa)/($GammaDesign_kNm3*$H_m))")
    $null = $sb.AppendLine("! <BEGIN:AnalysisOptions>")
    $null = $sb.AppendLine("! failure_criterion MohrCoulomb")
    $null = $sb.AppendLine("! material_type Isotropic")
    $null = $sb.AppendLine("! mesh_type T3")
    $null = $sb.AppendLine("! solver_executable p610_t3_mohr_coulomb")
    $null = $sb.AppendLine("! expected_FoS $(Format-Double $ExpectedFoS)")
    $null = $sb.AppendLine("! expected_FoS_tolerance_pct $(Format-Double $ToleranceFoSPct)")
    $null = $sb.AppendLine("! expected_FoS_mode srf")
    $null = $sb.AppendLine("! srf_F $(Format-Double $SrfF)")
    $null = $sb.AppendLine("! slope_geometry taylor_phi$([int]$PhiDeg)_vertical_srf")
    $null = $sb.AppendLine("! slope_angle_deg 90.0")
    $null = $sb.AppendLine("! slope_height_m $(Format-Double $H_m)")
    $null = $sb.AppendLine("! <END:AnalysisOptions>")

    $null = $sb.AppendLine("$nEls  $nn  1")
    # Material row uses DESIGN values; the C++ srf shim divides phi/c/psi by F.
    $null = $sb.AppendLine("$(Format-Double $PhiDeg)  $(Format-Double $C_kPa)  $(Format-Double $PhiDeg)  $(Format-Double $GammaDesign_kNm3)  $(Format-Double $E_kPa)  $(Format-Double $Nu)  1.0")

    foreach ($n in $nodes) {
        $null = $sb.AppendLine("$(Format-Double $n.X)  $(Format-Double $n.Y)")
    }
    foreach ($e in $elems) {
        $null = $sb.AppendLine("$($e.N1)  $($e.N2)  $($e.N3)")
    }
    $null = $sb.AppendLine($nr.ToString())
    foreach ($line in $bcLines) {
        $null = $sb.AppendLine($line)
    }
    $null = $sb.AppendLine("2")
    $null = $sb.AppendLine("$crestNode  $toeNode")
    $null = $sb.AppendLine("0.001  $Limit  $Incs  1")
    $null = $sb.AppendLine($nExc.ToString())
    $batch = New-Object System.Collections.Generic.List[string]
    foreach ($eid in $excavateElems) {
        $batch.Add($eid.ToString()) | Out-Null
        if ($batch.Count -ge 10) {
            $null = $sb.AppendLine(($batch -join '  '))
            $batch.Clear()
        }
    }
    if ($batch.Count -gt 0) {
        $null = $sb.AppendLine(($batch -join '  '))
    }
    [System.IO.File]::WriteAllText($OutPath, $sb.ToString())
    Write-Host ("Wrote {0}  (srf_F = {1:F2}, c_eff = {2:F2}, {3} nodes, {4} elements, {5} excavated)" -f $OutPath, $SrfF, ($C_kPa / $SrfF), $nn, $nEls, $nExc)
}

$geoRoot = Join-Path $RepoRoot 'Verification\multi_element\geotech'
$srfDir = Join-Path $geoRoot 'slope_srf_taylor_phi0_t3'
if (-not (Test-Path $srfDir)) { New-Item -ItemType Directory -Path $srfDir | Out-Null }

# F values bracket the analytical FoS_planar = 4·c/(γ·H) = 1.0.
# At F < ~0.5 the elastic stress field stays below yield (max_plastic_strain = 0).
# At F >= ~0.5 yield saturates and plastic strain accumulates. The orchestrator
# locates this elastic-to-plastic transition and reports it as srf_FoS_FE.
$Fvals = @(0.30, 0.50, 0.70, 1.00, 1.50, 2.00, 3.00)
foreach ($F in $Fvals) {
    $tag = ("{0:000}" -f [int][math]::Round($F * 100))
    $name = "slope_srf_taylor_phi0_t3_F$tag"
    New-SlopeSrfDat `
        -OutPath (Join-Path $srfDir ($name + '.dat')) `
        -PhiDeg 0.0 `
        -C_kPa 10.0 `
        -GammaDesign_kNm3 20.0 `
        -SrfF $F `
        -ExpectedFoS 1.0 `
        -ToleranceFoSPct 50.0 `
        -H_m 2.0 -X_toe_m 3.0 -L_m 8.0 -D_m 4.0 -Nx 16 -Ny 8
}
