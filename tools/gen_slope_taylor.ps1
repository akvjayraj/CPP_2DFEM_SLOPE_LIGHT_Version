<#
.SYNOPSIS
  Generate Verification/geotech/slope_taylor_*/.dat (vertical-cut cohesive slope,
  Taylor stability problem, gravity lambda-sweep -- M2.2 Option A).

.DESCRIPTION
  Builds a structured rectangular T3 mesh with a vertical-cut excavation:

      pre-excavation domain:   [0, L] x [-D, 0]
      excavated wedge (layer 1): [0, x_toe] x [-H, 0]      (vertical face at x=x_toe)
      remaining body after stage 1:
          [0, L]   x [-D, -H]    (foundation under slope)
          [x_toe, L] x [-H, 0]   (back slope)

  The C++ p610_t3_elastoplastic solver (and its constitutive variants) computes
  in-situ stresses sigma_yy = -gamma * (y_top - y_centroid) on every element at
  Stage 0, then for Stage 1 releases the in-situ stress + self-weight of the
  excavated wedge onto the remaining domain (see exc_loads loop in
  library/solver/p610_t3_elastoplastic.cpp). This gives a textbook
  excavation-driven slope-stability analysis with no solver changes required.

  ============================================================================
  Verification strategy (Option A: gravity lambda-sweep, no C++ changes)
  ============================================================================

  Each folder contains the SAME mesh repeated as N .dat files, only differing
  by the lambda factor applied to the material gamma (column 4 of the material
  row). Lambda scales the in-situ stress field and the body force linearly,
  so the Mohr-Coulomb FoS with respect to the original geometry becomes:

      FoS(lambda) = FoS_design / lambda

  We pick H, c, gamma so the closed-form planar-slip FoS at lambda = 1.0
  equals expected_FoS (typically 1.0 for the boundary case, or 1.5 for a
  well-stable case). The orchestrator (tools/build_all_slope_smoke_packages.ps1)
  then runs every lambda variant, classifies each as converged / non-converged
  via the .res "Inc N took M iterations" lines, and estimates the FE collapse
  multiplier as the midpoint between the highest converged lambda and the
  lowest non-converged lambda. Verdict:

      |lambda_collapse_FE - expected_FoS| / expected_FoS  <=  tolerance%

  ============================================================================
  Closed-form reference (Taylor 1937 / planar-slip upper bound)
  ============================================================================

  For a vertical cut of height H in a purely cohesive (phi=0) material with
  unit weight gamma and undrained shear strength c, the planar-slip (theta=45)
  upper-bound FoS is:

      FoS_planar = 4 * c / (gamma * H)

  This is the SHARPEST closed-form for vertical cuts and gives a clean target
  the FE viscoplastic solver should reproduce within ~15 %. (Circular slip
  drops the bound to FoS_circ = c / (gamma * H * Ns) with Ns ~= 0.261, i.e.
  about 3.83 c / (gamma H); the difference is well inside the lambda-sweep
  resolution we use here.)

.PARAMETER RepoRoot
  Optional repository root; defaults to the parent of this script.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/gen_slope_taylor.ps1
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

function New-SlopeTaylorDat {
    param(
        [string] $OutPath,
        [string] $Title,
        [double] $PhiDeg,
        [double] $C_kPa,
        [double] $GammaDesign_kNm3,
        [double] $LambdaFactor,
        [double] $ExpectedFoS,
        [double] $ToleranceFoSPct,
        [double] $H_m         = 2.0,    # excavation depth (slope height)
        [double] $X_toe_m     = 3.0,    # x of vertical face (toe at (X_toe, -H))
        [double] $L_m         = 8.0,    # full domain width
        [double] $D_m         = 4.0,    # full domain depth (D >= H)
        [int]    $Nx          = 16,
        [int]    $Ny          = 8,
        [double] $E_kPa       = 1.0e4,
        [double] $Nu          = 0.3,
        [int]    $Incs        = 20,
        [int]    $Limit       = 250
    )

    if ($D_m -lt $H_m + 1e-9) { throw "D ($D_m) must be >= H ($H_m)" }

    # Build a structured rectangular grid that ALIGNS y = -H exactly on a
    # mesh row, and x = X_toe exactly on a mesh column. We do this by snapping
    # Nx/Ny so that those interfaces sit on row/column indices.
    $iToe = [int][math]::Round($Nx * ($X_toe_m / $L_m))
    if ($iToe -lt 1 -or $iToe -ge $Nx) { throw "X_toe ($X_toe_m) does not land cleanly inside [0, L=$L_m] for Nx=$Nx" }
    $jH = [int][math]::Round($Ny * (1.0 - $H_m / $D_m))
    if ($jH -lt 1 -or $jH -ge $Ny) { throw "H ($H_m) does not land cleanly inside [0, D=$D_m] for Ny=$Ny" }

    # Uniform x and y nodes (no grading - the slope is small enough at this
    # resolution that uniform spacing keeps element shapes regular and the
    # solver well-conditioned).
    $xs = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -le $Nx; $i++) {
        $xs.Add(($i / [double] $Nx) * $L_m) | Out-Null
    }
    $ys = New-Object System.Collections.Generic.List[double]
    for ($j = 0; $j -le $Ny; $j++) {
        # j=0 -> bottom (y=-D), j=Ny -> top (y=0)
        $ys.Add(-((($Ny - $j) / [double] $Ny) * $D_m)) | Out-Null
    }

    # Build node list (column-major: y outer loop, x inner) and lookup.
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

    # Build T3 elements: two per quad (SW-NE diagonal, CCW).
    $elems         = New-Object System.Collections.Generic.List[object]
    $excavateElems = New-Object System.Collections.Generic.List[int]
    $elId = 1
    for ($j = 0; $j -lt $Ny; $j++) {
        for ($i = 0; $i -lt $Nx; $i++) {
            $sw = $nodeIdx["$i,$j"]
            $se = $nodeIdx["$($i+1),$j"]
            $ne = $nodeIdx["$($i+1),$($j+1)"]
            $nw = $nodeIdx["$i,$($j+1)"]
            # Quad sits in cells (i, j) -> (i+1, j+1).
            # Mark BOTH triangles in this quad as excavated when the quad lies
            # inside the wedge:  i < iToe  AND  j >= jH.
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

    # BCs (BC flag convention from geom::formnf: 1 = free, 0 = fixed).
    #   - y = -D row (j=0):    ux = uy = fixed (rigid base).
    #   - x = 0    col (i=0):  ux fixed (left roller).
    #   - x = L    col (i=Nx): ux fixed (right roller / far-field).
    $seen = @{}
    function Add-BC-Slope {
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
        Add-BC-Slope -NodeId $nodeIdx["$i,0"] -FreeX 0 -FreeY 0 -Map $seen
    }
    for ($j = 0; $j -le $Ny; $j++) {
        Add-BC-Slope -NodeId $nodeIdx["0,$j"]   -FreeX 0 -FreeY 1 -Map $seen
        Add-BC-Slope -NodeId $nodeIdx["$Nx,$j"] -FreeX 0 -FreeY 1 -Map $seen
    }

    $bcLines = New-Object System.Collections.Generic.List[string]
    foreach ($nid in ($seen.Keys | Sort-Object {[int]$_})) {
        $fx = $seen[$nid].Fx
        $fy = $seen[$nid].Fy
        $bcLines.Add(("{0}  {1}  {2}" -f $nid, $fx, $fy)) | Out-Null
    }
    $nr = $bcLines.Count

    # nouts: track displacement at the toe of the slope (top-of-vertical-face
    # node = node at (X_toe, 0)) and the crest (node at (X_toe, -H)).
    $crestNode = $nodeIdx["$iToe,$Ny"]
    $toeNode   = $nodeIdx["$iToe,$jH"]
    $nouts     = 2
    $noutsLine = "$crestNode  $toeNode"

    # Apply lambda only to gamma. E, nu, phi, c stay at design values.
    $gammaEff = $LambdaFactor * $GammaDesign_kNm3

    # Closed-form planar-slip FoS for documentation in the .dat header.
    # FoS_planar = 4 c / (gamma_eff * H)   (theta = 45 critical for phi = 0)
    if ($PhiDeg -gt 1e-6) {
        $fosPlanar = "n/a (phi != 0; planar-slip closed-form is purely cohesive)"
    } else {
        $fp = 4.0 * $C_kPa / ($gammaEff * $H_m)
        $fosPlanar = ("{0:F4}  (= 4*c / (lambda*gamma*H) = 4*{1} / ({2}*{3}*{4}))" -f $fp, $C_kPa, $LambdaFactor, $GammaDesign_kNm3, $H_m)
    }

    # === Compose .dat ============================================================
    $sb = New-Object System.Text.StringBuilder
    $null = $sb.AppendLine("! <solver:p610_t3>")
    $null = $sb.AppendLine("! Taylor vertical-cut cohesive slope (T3, plane strain) - lambda = $(Format-Double $LambdaFactor)")
    $null = $sb.AppendLine("!")
    $null = $sb.AppendLine("! Pre-excavation domain: [0, $L_m] m x [-$D_m, 0] m, $Nx x $Ny graded quads -> $nEls T3 elements ($nn nodes).")
    $null = $sb.AppendLine("! Excavation (layer 1): wedge x in [0, $X_toe_m] m, y in [-$H_m, 0] m -> $nExc T3 elements removed.")
    $null = $sb.AppendLine("! BCs: y=-$D_m rigid base; x=0 and x=$L_m roller (ux=0).")
    $null = $sb.AppendLine("!")
    $null = $sb.AppendLine("! Material (design + lambda * gamma): phi=$PhiDeg deg, c=$C_kPa kPa, gamma=$gammaEff kN/m^3, E=$E_kPa kPa, nu=$Nu.")
    $null = $sb.AppendLine("! Closed-form planar-slip FoS: $fosPlanar")
    $null = $sb.AppendLine("!")
    $null = $sb.AppendLine("! Verification (Option A - gravity lambda-sweep):")
    $null = $sb.AppendLine("!   tools/build_all_slope_smoke_packages.ps1 runs every lambda variant in this")
    $null = $sb.AppendLine("!   folder, classifies each .res as converged (max iters < limit) or not, and")
    $null = $sb.AppendLine("!   reports lambda_collapse_FE = midpoint(max_converged_lambda, min_diverged_lambda).")
    $null = $sb.AppendLine("!   Verdict PASS when |lambda_collapse_FE - expected_FoS|/expected_FoS <= tolerance.")
    $null = $sb.AppendLine("! <BEGIN:AnalysisOptions>")
    $null = $sb.AppendLine("! failure_criterion MohrCoulomb")
    $null = $sb.AppendLine("! material_type Isotropic")
    $null = $sb.AppendLine("! mesh_type T3")
    $null = $sb.AppendLine("! solver_executable p610_t3_mohr_coulomb")
    $null = $sb.AppendLine("! expected_FoS $(Format-Double $ExpectedFoS)")
    $null = $sb.AppendLine("! expected_FoS_tolerance_pct $(Format-Double $ToleranceFoSPct)")
    $null = $sb.AppendLine("! expected_FoS_mode lambda_sweep")
    $null = $sb.AppendLine("! lambda_factor $(Format-Double $LambdaFactor)")
    $null = $sb.AppendLine("! slope_geometry taylor_phi$([int]$PhiDeg)_vertical")
    $null = $sb.AppendLine("! slope_angle_deg 90.0")
    $null = $sb.AppendLine("! slope_height_m $(Format-Double $H_m)")
    $null = $sb.AppendLine("! <END:AnalysisOptions>")

    $null = $sb.AppendLine("$nEls  $nn  1")
    # Material row: phi c psi gamma_eff E nu K0
    # psi = phi (associated flow); K0 = 1 (geostatic isotropic) so initial sigma_xx = sigma_yy.
    $null = $sb.AppendLine("$(Format-Double $PhiDeg)  $(Format-Double $C_kPa)  $(Format-Double $PhiDeg)  $(Format-Double $gammaEff)  $(Format-Double $E_kPa)  $(Format-Double $Nu)  1.0")

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
    $null = $sb.AppendLine($nouts.ToString())
    $null = $sb.AppendLine($noutsLine)
    # Solver control: tol limit incs layers (layers = 1 -> excavation path).
    $null = $sb.AppendLine("0.001  $Limit  $Incs  1")
    # Layer 1 excavation list.
    $null = $sb.AppendLine($nExc.ToString())
    # Pack excavation IDs 10 per line for readability.
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
    Write-Host ("Wrote {0}  (lambda = {1:F2}, gamma_eff = {2:F2}, {3} nodes, {4} elements, {5} excavated)" -f $OutPath, $LambdaFactor, $gammaEff, $nn, $nEls, $nExc)
}

# ============================================================================
# Fixture set: vertical cut, phi = 0, gamma * H / c = 4 -> planar-slip FoS = 1.0
# ============================================================================
$geoRoot = Join-Path $RepoRoot 'Verification\multi_element\geotech'
$slopeDir = Join-Path $geoRoot 'slope_taylor_phi0_t3'
if (-not (Test-Path $slopeDir)) { New-Item -ItemType Directory -Path $slopeDir | Out-Null }

# Lambda values bracket the expected collapse multiplier (1.0).
$lambdas = @(0.50, 0.70, 0.85, 1.00, 1.15, 1.30, 1.50)
foreach ($lam in $lambdas) {
    $tag = ("{0:000}" -f [int][math]::Round($lam * 100))
    $name = "slope_taylor_phi0_t3_lambda$tag"
    New-SlopeTaylorDat `
        -OutPath (Join-Path $slopeDir ($name + '.dat')) `
        -Title $name `
        -PhiDeg 0.0 `
        -C_kPa 10.0 `
        -GammaDesign_kNm3 20.0 `
        -LambdaFactor $lam `
        -ExpectedFoS 1.0 `
        -ToleranceFoSPct 25.0 `
        -H_m 2.0 -X_toe_m 3.0 -L_m 8.0 -D_m 4.0 -Nx 16 -Ny 8
}

# ============================================================================
# Frictional fixture set (M2.3 Slice C): phi = 20 deg, c = 10 kPa, gamma = 20 kN/m^3.
# Same lambda-sweep diagnostic as the phi = 0 set; the orchestrator reports the
# yield-saturation lambda. Taylor stability number Ns(phi=20, beta=90) ~= 0.181
# yields a circular-slip FoS ~= 10 / (20 * 2 * 0.181) = 1.38 at lambda = 1.0,
# which matches a typical "slightly stable" frictional vertical cut.
# ============================================================================
$slopeDir20 = Join-Path $geoRoot 'slope_taylor_phi20_t3'
if (-not (Test-Path $slopeDir20)) { New-Item -ItemType Directory -Path $slopeDir20 | Out-Null }

foreach ($lam in $lambdas) {
    $tag = ("{0:000}" -f [int][math]::Round($lam * 100))
    $name = "slope_taylor_phi20_t3_lambda$tag"
    New-SlopeTaylorDat `
        -OutPath (Join-Path $slopeDir20 ($name + '.dat')) `
        -Title $name `
        -PhiDeg 20.0 `
        -C_kPa 10.0 `
        -GammaDesign_kNm3 20.0 `
        -LambdaFactor $lam `
        -ExpectedFoS 1.38 `
        -ToleranceFoSPct 30.0 `
        -H_m 2.0 -X_toe_m 3.0 -L_m 8.0 -D_m 4.0 -Nx 16 -Ny 8
}
