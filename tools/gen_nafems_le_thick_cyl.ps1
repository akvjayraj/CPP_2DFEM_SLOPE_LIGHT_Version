#requires -Version 5.1
<#
.SYNOPSIS
  Generate quarter-symmetric thick-cylinder .dat files for the NAFEMS
  LE10 / LE11 (Lame closed-form) verification cases, in T3 and Q4 variants.

.DESCRIPTION
  Builds a structured `nr x ns` mesh of quadrilaterals on the quarter annulus
  a <= r <= b, 0 <= theta <= pi/2 and writes it to disk in the p610_t3_*
  .dat format with a `! <BEGIN:AnalysisOptions>` block declaring the Lame
  expected radial-field that the GUI / orchestrator can compare against.

  Boundary conditions:
    * y-fixed (uy=0) on the bottom edge (theta=0, symmetry)
    * x-fixed (ux=0) on the left edge (theta=pi/2, symmetry)
  Loading:
    * Single mode-0 traction group with sigma = (-p, -p, 0) applied to the
      INNER arc (r=a). With sigma=-p*I, T = sigma . n = -p*n on every edge,
      regardless of orientation, so a single group covers the curved boundary.
    * Inner-arc edges are listed in DECREASING-theta order so the right-hand
      normal points outward from the domain (i.e. into the hole), matching
      the existing patch-test convention.
#>
param(
    [string] $OutputRoot = '',
    [double] $InnerRadius = 1.0,
    [double] $OuterRadius = 2.0,
    [double] $Pressure    = 100.0,
    [double] $YoungE      = 1.0e6,
    [double] $PoissonNu   = 0.25,
    [int]    $Nr          = 4,
    [int]    $Ns          = 6
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'Verification\multi_element' }

function Format-Double { param([double] $v) return $v.ToString([System.Globalization.CultureInfo]::InvariantCulture) }

function Build-Dat {
    param(
        [string] $MeshType,
        [string] $CaseName,
        [double] $a, [double] $b, [double] $pressure,
        [double] $E, [double] $nu,
        [int] $nr, [int] $ns,
        [bool] $planeStrain
    )

    $sb = New-Object System.Text.StringBuilder

    # Pre-compute node coordinates: id = i*(ns+1) + j + 1
    $nn = ($nr + 1) * ($ns + 1)
    $nodes = New-Object 'object[]' $nn
    for ($i = 0; $i -le $nr; $i++) {
        $r = $a + $i * ($b - $a) / $nr
        for ($j = 0; $j -le $ns; $j++) {
            $theta = $j * ([math]::PI / 2.0) / $ns
            $idx = $i * ($ns + 1) + $j
            $nodes[$idx] = @{
                X = $r * [math]::Cos($theta)
                Y = $r * [math]::Sin($theta)
            }
        }
    }

    $nElsQ4 = $nr * $ns
    $nElsT3 = $nElsQ4 * 2
    $nEls = if ($MeshType -eq 'Q4') { $nElsQ4 } else { $nElsT3 }

    $sb.AppendLine('! <solver:p610_t3>') | Out-Null
    $sb.AppendLine("! NAFEMS-style $CaseName ($MeshType, plane strain, Lame thick-cylinder)") | Out-Null
    $sb.AppendLine("!") | Out-Null
    $sb.AppendLine("! Quarter-symmetric thick cylinder under internal pressure p = $pressure kPa.") | Out-Null
    $sb.AppendLine("!   a = $a m, b = $b m, E = $E kPa, nu = $nu") | Out-Null
    $sb.AppendLine("!   nr x ns = $nr x $ns ($nEls $MeshType elements, $nn nodes)") | Out-Null
    $sb.AppendLine("!") | Out-Null
    $sb.AppendLine("! BCs (symmetry):") | Out-Null
    $sb.AppendLine("!   bottom edge (y=0, j=0):  uy fixed") | Out-Null
    $sb.AppendLine("!   left   edge (x=0, j=ns): ux fixed") | Out-Null
    $sb.AppendLine("!") | Out-Null
    $sb.AppendLine("! Loading:") | Out-Null
    $sb.AppendLine("!   Internal pressure applied as mode-0 sigma=(-p, -p, 0) traction on the") | Out-Null
    $sb.AppendLine("!   inner arc; T=sigma.n=-p*n is independent of edge orientation.") | Out-Null
    $sb.AppendLine("!") | Out-Null
    $sb.AppendLine("! Closed-form (Lame, plane strain, tension-positive):") | Out-Null
    $sb.AppendLine("!   sigma_rr(r) = p*a^2/(b^2-a^2) * (1 - b^2/r^2)") | Out-Null
    $sb.AppendLine("!   sigma_tt(r) = p*a^2/(b^2-a^2) * (1 + b^2/r^2)") | Out-Null
    $sb.AppendLine("!   sigma_zz    = nu*(sigma_rr + sigma_tt)   [plane strain]") | Out-Null
    $sb.AppendLine("! <BEGIN:AnalysisOptions>") | Out-Null
    $sb.AppendLine("! failure_criterion MohrCoulomb") | Out-Null
    $sb.AppendLine("! material_type Isotropic") | Out-Null
    $sb.AppendLine("! mesh_type $MeshType") | Out-Null
    $sb.AppendLine("! solver_executable p610_t3_elastoplastic") | Out-Null
    $sb.AppendLine("! expected_radial_mode lame_pressurized_cylinder") | Out-Null
    $sb.AppendLine("! expected_radial_inner_a $(Format-Double $a)") | Out-Null
    $sb.AppendLine("! expected_radial_outer_b $(Format-Double $b)") | Out-Null
    $sb.AppendLine("! expected_radial_pressure_kPa $(Format-Double $pressure)") | Out-Null
    $sb.AppendLine("! expected_radial_nu $(Format-Double $nu)") | Out-Null
    $sb.AppendLine("! expected_radial_center_x 0") | Out-Null
    $sb.AppendLine("! expected_radial_center_y 0") | Out-Null
    $psStr = if ($planeStrain) { 'true' } else { 'false' }
    $sb.AppendLine("! expected_radial_plane_strain $psStr") | Out-Null
    # Tolerance: after the May-2026 Jacobian-transpose fix in
    # build_bee_at_gp() (library/solver/p610_t3_elastoplastic.cpp), the
    # quarter-symmetric annulus with inner-arc traction matches Lame to a few
    # kPa on Q4 (~0.7) and ~17 kPa on the 192-element T3 mesh. The Q4 default
    # is 5 kPa; T3 is 25 kPa to absorb the linear-strain discretisation error.
    $tol = if ($meshType -eq 'Q4') { 5.0 } else { 25.0 }
    $sb.AppendLine("! expected_radial_tolerance_kPa $tol") | Out-Null
    $sb.AppendLine("! tolerance_kPa $tol") | Out-Null
    $sb.AppendLine("! <END:AnalysisOptions>") | Out-Null

    # Solver block
    $sb.AppendLine("$nEls  $nn  1") | Out-Null
    $sb.AppendLine("89  1.0e10  0  0  $(Format-Double $E)  $(Format-Double $nu)  0") | Out-Null
    foreach ($node in $nodes) {
        $sb.AppendLine("$(Format-Double $node.X)  $(Format-Double $node.Y)") | Out-Null
    }

    # Connectivity
    if ($MeshType -eq 'Q4') {
        for ($i = 0; $i -lt $nr; $i++) {
            for ($j = 0; $j -lt $ns; $j++) {
                $n1 = $i       * ($ns + 1) + $j     + 1
                $n2 = ($i + 1) * ($ns + 1) + $j     + 1
                $n3 = ($i + 1) * ($ns + 1) + $j + 1 + 1
                $n4 = $i       * ($ns + 1) + $j + 1 + 1
                $sb.AppendLine("$n1  $n2  $n3  $n4") | Out-Null
            }
        }
    } else {
        for ($i = 0; $i -lt $nr; $i++) {
            for ($j = 0; $j -lt $ns; $j++) {
                $n1 = $i       * ($ns + 1) + $j     + 1
                $n2 = ($i + 1) * ($ns + 1) + $j     + 1
                $n3 = ($i + 1) * ($ns + 1) + $j + 1 + 1
                $n4 = $i       * ($ns + 1) + $j + 1 + 1
                $sb.AppendLine("$n1  $n2  $n3") | Out-Null
                $sb.AppendLine("$n1  $n3  $n4") | Out-Null
            }
        }
    }

    # Boundary conditions
    $bcLines = New-Object 'System.Collections.Generic.List[string]'
    for ($i = 0; $i -le $nr; $i++) {
        # bottom edge node (j=0): fix uy (y_free=0), x_free=1
        $node = $i * ($ns + 1) + 0 + 1
        $bcLines.Add("$node  1  0") | Out-Null
    }
    for ($i = 0; $i -le $nr; $i++) {
        # left edge node (j=ns): fix ux (x_free=0), y_free=1
        $node = $i * ($ns + 1) + $ns + 1
        $bcLines.Add("$node  0  1") | Out-Null
    }
    $sb.AppendLine("$($bcLines.Count)") | Out-Null
    foreach ($line in $bcLines) { $sb.AppendLine($line) | Out-Null }

    # Output node list (corners)
    $oCorners = @()
    $oCorners += (0     * ($ns + 1) + 0   + 1)  # (a,0)
    $oCorners += (0     * ($ns + 1) + $ns + 1)  # (0,a)
    $oCorners += ($nr   * ($ns + 1) + 0   + 1)  # (b,0)
    $oCorners += ($nr   * ($ns + 1) + $ns + 1)  # (0,b)
    $sb.AppendLine("$($oCorners.Count)") | Out-Null
    $sb.AppendLine(($oCorners -join '  ')) | Out-Null

    # tol limit incs layers
    $sb.AppendLine("0.0001  80  3  0") | Out-Null
    # 1 traction group, mode 0, sigma = (-p, -p, 0) on the INNER arc.
    # Lame closed-form (internal-pressure, no outer pressure, plane strain):
    #   sigma_rr(r) = p*a^2/(b^2-a^2) * (1 - b^2/r^2)
    #   sigma_tt(r) = p*a^2/(b^2-a^2) * (1 + b^2/r^2)
    #   sigma_zz    = nu*(sigma_rr + sigma_tt)
    # NOTE: with the present p610_t3_* solver the FE solution can deviate
    # from Lame by a non-trivial constant (mesh / quadrature interplay), so
    # the .dat header tolerance is set generously (200 kPa) to stay PASS
    # while still flagging gross regressions. Tightening this requires
    # solver-side investigation that is out of scope for this verification
    # plan.
    $sb.AppendLine("1") | Out-Null
    $negP = Format-Double (-1.0 * $pressure)
    $sb.AppendLine("0  $negP  $negP  0") | Out-Null

    # Inner-arc edges, DECREASING j (outward normal = into the hole)
    $innerLines = New-Object 'System.Collections.Generic.List[string]'
    for ($j = $ns; $j -ge 1; $j--) {
        $a1 = 0 * ($ns + 1) + $j           + 1
        $b1 = 0 * ($ns + 1) + ($j - 1)     + 1
        $innerLines.Add("$a1  $b1") | Out-Null
    }
    $sb.AppendLine("$($innerLines.Count)") | Out-Null
    foreach ($line in $innerLines) { $sb.AppendLine($line) | Out-Null }

    return $sb.ToString()
}

function Write-DatCase {
    param(
        [string] $Stem,
        [string] $MeshType,
        [bool]   $IsLE11
    )
    $dir = Join-Path $OutputRoot $Stem
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $datPath = Join-Path $dir "$Stem.dat"
    $caseTitle = if ($IsLE11) { "LE11 thick-cylinder (axial-restrained, plane strain)" }
                 else         { "LE10 thick-cylinder (plane-strain section)" }
    $content = Build-Dat `
        -MeshType $MeshType `
        -CaseName $caseTitle `
        -a $InnerRadius -b $OuterRadius -pressure $Pressure `
        -E $YoungE -nu $PoissonNu `
        -nr $Nr -ns $Ns `
        -planeStrain $true
    Set-Content -LiteralPath $datPath -Value $content -Encoding UTF8
    Write-Host "Wrote $datPath"
}

Write-DatCase -Stem 'nafems_le10_thick_cyl_t3'              -MeshType 'T3' -IsLE11 $false
Write-DatCase -Stem 'nafems_le10_thick_cyl_q4'              -MeshType 'Q4' -IsLE11 $false
Write-DatCase -Stem 'nafems_le11_thick_cyl_plane_strain_t3' -MeshType 'T3' -IsLE11 $true
Write-DatCase -Stem 'nafems_le11_thick_cyl_plane_strain_q4' -MeshType 'Q4' -IsLE11 $true
