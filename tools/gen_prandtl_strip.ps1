<#
.SYNOPSIS
  Generate Verification/geotech/prandtl_strip_phi{0,20}_t3/*.dat (NAFEMS-style
  Prandtl strip footing on a c-phi half-space).

.DESCRIPTION
  Builds a structured half-symmetry T3 mesh for the textbook Prandtl bearing
  capacity problem (Smith-Griffiths Program 6.2):

    - 0 <= x <= W (right boundary, far field), -H <= y <= 0 (top free surface).
    - Strip footing of half-width B/2 = 1 m on x in [0, B/2], y = 0.
    - Half-symmetry: x = 0 is the centerline (ux = 0).
    - BCs: ux = 0 on x = 0 and x = W; ux = uy = 0 on y = -H.
    - Loading: pseudo-static increasing pressure on the footing edge, applied
      via an mc_tunnel-style mode-0 traction block. We push the analysis past
      the analytical collapse load q_ult = c * Nc (+ q*Nq + 0.5*gamma*B*Ng,
      both zero here because gamma = q_surcharge = 0). The orchestrator parses
      the .res to find the first non-converging increment ("collapse increment")
      and reports FE q_ult / c versus the textbook Nc.

  The mesh is laid out as nx*ny rectangular cells under and beside the footing,
  each split into two T3 elements along the SW-NE diagonal so every quad
  contributes two CCW triangles. Refinement is biased near the footing corner
  via a geometric grading factor.

  Header schema (read by both the orchestrator and the GUI auto-trigger):
    ! expected_qult_per_c   <Nc value>     (e.g. 5.14 for phi=0, 14.83 for phi=20)
    ! expected_phi_deg      <phi>
    ! expected_c_kPa        <c>
    ! expected_qult_tolerance_pct <pct>    (default 5.0)

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/gen_prandtl_strip.ps1
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

function New-PrandtlDat {
    param(
        [string] $OutPath,
        [string] $Title,
        [double] $PhiDeg,
        [double] $C_kPa,
        [double] $Nc,
        [double] $E_kPa = 1.0e5,
        [double] $Nu = 0.3,
        [double] $HalfWidthB = 1.0,
        [int]    $Nx = 12,
        [int]    $Ny = 10,
        [double] $WidthW = 5.0,
        [double] $HeightH = 5.0,
        [int]    $Incs = 50,
        [int]    $Limit = 250,
        [double] $LoadFactorMax = 1.0,
        [double] $TolPct = 5.0,
        [ValidateSet('T3','Q4')]
        [string] $MeshType = 'T3'
    )

    # Mesh-graded x and y nodes biased toward the footing corner (origin).
    $xs = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -le $Nx; $i++) {
        $t = $i / [double]$Nx
        $tt = $t * $t
        $xs.Add($tt * $WidthW) | Out-Null
    }
    # ys[0] = -H (bottom), ys[Ny] = 0 (free surface). Geometric grading puts
    # finer rows near the surface where the footing acts.
    $ys = New-Object System.Collections.Generic.List[double]
    for ($j = 0; $j -le $Ny; $j++) {
        $t = ($Ny - $j) / [double]$Ny
        $tt = $t * $t
        $ys.Add(-$tt * $HeightH) | Out-Null
    }

    # Build node list (column-major: x outer loop, y inner) and lookup.
    $nodes = New-Object System.Collections.Generic.List[object]
    $nodeIdx = @{}  # key "$i,$j" -> 1-based id
    $idCounter = 1
    for ($j = 0; $j -le $Ny; $j++) {
        for ($i = 0; $i -le $Nx; $i++) {
            $nodes.Add([pscustomobject]@{ Id = $idCounter; X = $xs[$i]; Y = $ys[$j] }) | Out-Null
            $nodeIdx["$i,$j"] = $idCounter
            $idCounter++
        }
    }
    $nn = $nodes.Count

    # Build elements. T3 splits every quad into two CCW triangles along the
    # SW-NE diagonal; Q4 keeps the quad as a single CCW element.
    $elems = New-Object System.Collections.Generic.List[object]
    $elId = 1
    for ($j = 0; $j -lt $Ny; $j++) {
        for ($i = 0; $i -lt $Nx; $i++) {
            $sw = $nodeIdx["$i,$j"]
            $se = $nodeIdx["$($i+1),$j"]
            $ne = $nodeIdx["$($i+1),$($j+1)"]
            $nw = $nodeIdx["$i,$($j+1)"]
            if ($MeshType -eq 'Q4') {
                $elems.Add([pscustomobject]@{ Id = $elId; Nodes = @($sw, $se, $ne, $nw) }) | Out-Null
                $elId++
            } else {
                $elems.Add([pscustomobject]@{ Id = $elId; Nodes = @($sw, $se, $ne) }) | Out-Null
                $elId++
                $elems.Add([pscustomobject]@{ Id = $elId; Nodes = @($sw, $ne, $nw) }) | Out-Null
                $elId++
            }
        }
    }
    $nEls = $elems.Count
    $nodesPerEl = if ($MeshType -eq 'Q4') { 4 } else { 3 }

    # BCs:
    #   - x=0 column (i=0): ux fixed (symmetry) on every j except origin.
    #   - x=W column (i=Nx): ux fixed (far-field roller).
    #   - y=-H row (j=0):    ux=uy fixed (rigid base).
    #   - y=0 row (j=Ny):    free unless overridden by the above.
    # BC flag convention: 1=free, 0=fixed (per geom::formnf).
    $bcLines = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    function Add-BC {
        param([int] $NodeId, [int] $FreeX, [int] $FreeY, [hashtable] $Map, [System.Collections.Generic.List[string]] $Lines)
        if ($Map.ContainsKey($NodeId)) {
            $cur = $Map[$NodeId]
            $newFx = if ($FreeX -eq 0 -or $cur.Fx -eq 0) { 0 } else { 1 }
            $newFy = if ($FreeY -eq 0 -or $cur.Fy -eq 0) { 0 } else { 1 }
            $Map[$NodeId] = @{ Fx = $newFx; Fy = $newFy }
        } else {
            $Map[$NodeId] = @{ Fx = $FreeX; Fy = $FreeY }
        }
    }

    # Bottom row: ux=uy fixed.
    for ($i = 0; $i -le $Nx; $i++) {
        $nid = $nodeIdx["$i,0"]
        Add-BC -NodeId $nid -FreeX 0 -FreeY 0 -Map $seen -Lines $bcLines
    }
    # Left (symmetry): ux fixed.
    for ($j = 0; $j -le $Ny; $j++) {
        $nid = $nodeIdx["0,$j"]
        Add-BC -NodeId $nid -FreeX 0 -FreeY 1 -Map $seen -Lines $bcLines
    }
    # Right (far-field): ux fixed.
    for ($j = 0; $j -le $Ny; $j++) {
        $nid = $nodeIdx["$Nx,$j"]
        Add-BC -NodeId $nid -FreeX 0 -FreeY 1 -Map $seen -Lines $bcLines
    }

    $bcSorted = $seen.Keys | Sort-Object {[int]$_}
    foreach ($nid in $bcSorted) {
        $fx = $seen[$nid].Fx
        $fy = $seen[$nid].Fy
        $bcLines.Add(("{0}  {1}  {2}" -f $nid, $fx, $fy)) | Out-Null
    }
    $nr = $bcLines.Count

    # Footing edges along y=0 from x=0 to x=B/2.
    $footingEdges = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Nx; $i++) {
        $xL = $xs[$i]
        $xR = $xs[$i + 1]
        if ($xR -le $HalfWidthB + 1e-9) {
            # CCW orientation: edge i+1 -> i so that outward normal = (0, +1).
            $a = $nodeIdx["$($i+1),$Ny"]
            $b = $nodeIdx["$i,$Ny"]
            $footingEdges.Add(("{0}  {1}" -f $a, $b)) | Out-Null
        }
    }
    $nFootingEdges = $footingEdges.Count

    # Total applied pressure (mode-0 traction block uses sigma so apply syy = -p).
    $qUlt = $C_kPa * $Nc
    $pApplied = $LoadFactorMax * $qUlt

    # === Compose .dat ============================================================
    $sb = New-Object System.Text.StringBuilder
    $null = $sb.AppendLine("! <solver:p610_t3>")
    $null = $sb.AppendLine("! NAFEMS-style Prandtl strip footing ($MeshType, plane strain, c-phi half-space)")
    $null = $sb.AppendLine("!")
    $null = $sb.AppendLine("! Half-symmetry domain $($WidthW) m wide x $($HeightH) m deep, footing half-width B/2 = $HalfWidthB m.")
    $meshDesc = if ($MeshType -eq 'Q4') { "$Nx x $Ny graded quads -> $nEls Q4 elements" } else { "$Nx x $Ny graded quads -> $nEls T3 elements (SW-NE split)" }
    $null = $sb.AppendLine("! Mesh: $meshDesc ($nn nodes).")
    $null = $sb.AppendLine("! BCs: y=-H rigid; x=0 symmetry roller; x=W far-field roller.")
    $null = $sb.AppendLine("! Loading: incremental footing pressure up to $LoadFactorMax * q_ult = $(Format-Double $pApplied) kPa.")
    $null = $sb.AppendLine("!")
    $null = $sb.AppendLine("! Closed form (Terzaghi-Vesic, gamma = q_surcharge = 0):")
    $null = $sb.AppendLine("!   q_ult = c * Nc                    -> Nc(phi=$PhiDeg) = $(Format-Double $Nc)")
    $null = $sb.AppendLine("!   With c = $C_kPa kPa, q_ult = $(Format-Double $qUlt) kPa.")
    $null = $sb.AppendLine("!")
    $null = $sb.AppendLine("! Verification approach (Smith-Griffiths VP solver constraints):")
    $null = $sb.AppendLine("!   p_applied is fixed at the analytical q_ult = c * Nc. The solver must")
    $null = $sb.AppendLine("!   (a) converge in every increment (no `iters == limit`), and")
    $null = $sb.AppendLine("!   (b) reproduce footing-element syy ~= -q_ult within tolerance %% (load")
    $null = $sb.AppendLine("!       transfer check) while showing plastic flow (s1-s3 ~= 2 c at yield).")
    $null = $sb.AppendLine("!   Identifying the exact collapse load (FE q_ult bracket) requires multi-run")
    $null = $sb.AppendLine("!   load-stepping that is out of scope for the current single-shot solver.")
    $null = $sb.AppendLine("! <BEGIN:AnalysisOptions>")
    $null = $sb.AppendLine("! failure_criterion MohrCoulomb")
    $null = $sb.AppendLine("! material_type Isotropic")
    $null = $sb.AppendLine("! mesh_type $MeshType")
    $null = $sb.AppendLine("! solver_executable p610_t3_elastoplastic")
    $null = $sb.AppendLine("! expected_qult_per_c $(Format-Double $Nc)")
    $null = $sb.AppendLine("! expected_phi_deg $(Format-Double $PhiDeg)")
    $null = $sb.AppendLine("! expected_c_kPa $(Format-Double $C_kPa)")
    $null = $sb.AppendLine("! expected_qult_tolerance_pct $(Format-Double $TolPct)")
    $null = $sb.AppendLine("! expected_p_applied_kPa $(Format-Double $pApplied)")
    $null = $sb.AppendLine("! <END:AnalysisOptions>")

    $null = $sb.AppendLine("$nEls  $nn  1")
    # Material: phi c psi gamma E nu K0 (Smith-Griffiths layout).
    # Use psi=0 (non-associated) for phi=20 to keep things conservative; for
    # phi=0 psi=0 is the only option.
    $psi = 0.0
    $null = $sb.AppendLine("$(Format-Double $PhiDeg)  $(Format-Double $C_kPa)  $(Format-Double $psi)  0  $(Format-Double $E_kPa)  $(Format-Double $Nu)  0")
    foreach ($n in $nodes) {
        $null = $sb.AppendLine("$(Format-Double $n.X)  $(Format-Double $n.Y)")
    }
    foreach ($e in $elems) {
        $null = $sb.AppendLine(($e.Nodes -join '  '))
    }
    $null = $sb.AppendLine($nr.ToString())
    foreach ($line in $bcLines) {
        $null = $sb.AppendLine($line)
    }
    # nouts list: one node per row at footing corner (used only for printing).
    $cornerNode = $nodeIdx["0,$Ny"]
    $null = $sb.AppendLine("1")
    $null = $sb.AppendLine($cornerNode.ToString())
    # Solver control: tol limit incs layers
    $null = $sb.AppendLine("0.001  $Limit  $Incs  0")
    # Traction: 1 group with sxx=0, syy=-p, sxy=0 (downward pressure on edge with n=(0,+1)).
    $null = $sb.AppendLine("1")
    $null = $sb.AppendLine("0  0  $(Format-Double (-$pApplied))  0")
    $null = $sb.AppendLine($nFootingEdges.ToString())
    foreach ($edge in $footingEdges) {
        $null = $sb.AppendLine($edge)
    }
    [System.IO.File]::WriteAllText($OutPath, $sb.ToString())
    Write-Host ("Wrote {0}  ({1} nodes, {2} elements, p_applied = {3:F2} kPa)" -f $OutPath, $nn, $nEls, $pApplied)
}

$geoRoot = Join-Path $RepoRoot 'Verification\multi_element\geotech'

# Phi = 0: pure cohesion. Nc = 2 + pi = 5.14159265.
$dir0 = Join-Path $geoRoot 'prandtl_strip_phi0_t3'
if (-not (Test-Path $dir0)) { New-Item -ItemType Directory -Path $dir0 | Out-Null }
New-PrandtlDat -OutPath (Join-Path $dir0 'prandtl_strip_phi0_t3.dat') `
    -Title 'Prandtl strip footing phi=0' `
    -PhiDeg 0.0 -C_kPa 10.0 -Nc 5.14159265

# Phi = 20: Vesic Nc(20) = 14.83. We use the textbook value.
$dir20 = Join-Path $geoRoot 'prandtl_strip_phi20_t3'
if (-not (Test-Path $dir20)) { New-Item -ItemType Directory -Path $dir20 | Out-Null }
New-PrandtlDat -OutPath (Join-Path $dir20 'prandtl_strip_phi20_t3.dat') `
    -Title 'Prandtl strip footing phi=20' `
    -PhiDeg 20.0 -C_kPa 10.0 -Nc 14.83

# M2.3 Slice C: non-T3 element lane. The same Prandtl phi=0 problem on a Q4
# mesh exercises the geotech bearing-capacity flow (load-transfer + yield-ratio
# checks) on quadrilateral elements, broadening the matrix beyond T3-only
# coverage.
$dir0Q4 = Join-Path $geoRoot 'prandtl_strip_phi0_q4'
if (-not (Test-Path $dir0Q4)) { New-Item -ItemType Directory -Path $dir0Q4 | Out-Null }
New-PrandtlDat -OutPath (Join-Path $dir0Q4 'prandtl_strip_phi0_q4.dat') `
    -Title 'Prandtl strip footing phi=0 (Q4)' `
    -PhiDeg 0.0 -C_kPa 10.0 -Nc 5.14159265 -MeshType 'Q4'
