<#
.SYNOPSIS
  Generate SLOPE64 Example 1 p610_t3 .dat (homogeneous embankment) with SLOPE64 mesh counts.

.DESCRIPTION
  Ports emb_2d_geom / emb_2d_bc (Smith & Griffiths Program 6.4) to emit a T3 mesh
  (each Q8 split into two triangles on corners). Coordinates are mapped to y-up
  (GUI / SLOPE64 manual convention): y_gui = y_sg + (h1 + h2) so base y=0, crest y=h1+h2.

  Solver: layers=1, zero excavation elements, gravity via geostatic stress + body-force
  equilibration (requires p610_t3 patch for noexe==0).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/gen_slope64_ex1.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $OutPath,
    [int] $Nx1 = 32,
    [int] $Nx2 = 12,
    [int] $Ny1 = 10,
    [int] $Ny2 = 10,
    [double] $SrfF = 1.10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
if (-not $OutPath) {
    $OutPath = Join-Path $RepoRoot 'scripts\slope_stability_T3_SRF\ex1_homogeneous_slope.dat'
}

$w1 = 12.0; $s1 = 20.0; $w2 = 12.0; $h1 = 10.0; $h2 = 10.0

function Invoke-Emb2dGeom {
    param([int]$iel, [int]$nx1, [int]$nx2, [int]$ny1, [int]$ny2,
          [double]$w1, [double]$s1, [double]$w2, [double]$h1, [double]$h2)

    $nxe = $nx1 + $nx2
    $nye = $ny1 + $ny2
    $nt = $nx1 * $ny1
    $nc = (3 * $nye + 2) * $nx1 + 2 * $ny1
    $facx = $s1 / $ny1
    $facy = $h1 / $ny1
    $facb = ($w1 + $s1) / $nx1
    $facs = if ($ny2 -ne 0) { $h2 / $ny2 } else { 0.0 }
    $frh  = if ($nx2 -ne 0) { $w2 / $nx2 } else { 0.0 }

    $coord = New-Object 'double[,]' 2, 8
    $num = New-Object int[] 8

    if ($iel -le $nt) {
        $iq = [int]([math]::Floor(($iel - 1) / $nx1)) + 1
        $ip = $iel - ($iq - 1) * $nx1
    } else {
        $iq = [int]([math]::Floor(($iel - $nt - 1) / $nxe)) + $ny1 + 1
        $ip = $iel - $nt - ($iq - $ny1 - 1) * $nxe
    }

    if ($ip -le $nx1) {
        $num[0] = ($ip - 1) * (3 * $nye + 2) + 2 * $iq + 1
        $num[1] = $num[0] - 1
        $num[2] = $num[0] - 2
        $num[3] = ($ip - 1) * (3 * $nye + 2) + 2 * $nye + $iq + 1
        $num[4] = $ip * (3 * $nye + 2) + 2 * $iq - 1
        $num[5] = $num[4] + 1
        $num[6] = $num[4] + 2
        $num[7] = $num[3] + 1

        if ($iq -le $ny1) {
            $coord[0,0] = ($ip - 1) * ($w1 + $iq * $facx) / $nx1
            $coord[0,2] = ($ip - 1) * ($w1 + ($iq - 1) * $facx) / $nx1
            $coord[0,4] = $ip * ($w1 + ($iq - 1) * $facx) / $nx1
            $coord[0,6] = $ip * ($w1 + $iq * $facx) / $nx1
            $coord[1,0] = -$iq * $facy
            $coord[1,2] = -($iq - 1) * $facy
            $coord[1,4] = -($iq - 1) * $facy
            $coord[1,6] = -$iq * $facy
        } else {
            $coord[0,0] = ($ip - 1) * $facb
            $coord[0,2] = ($ip - 1) * $facb
            $coord[0,4] = $ip * $facb
            $coord[0,6] = $ip * $facb
            $coord[1,0] = -$h1 - ($iq - $ny1) * $facs
            $coord[1,2] = -$h1 - ($iq - $ny1 - 1) * $facs
            $coord[1,4] = -$h1 - ($iq - $ny1 - 1) * $facs
            $coord[1,6] = -$h1 - ($iq - $ny1) * $facs
        }
    } else {
        $num[0] = $nc + ($ip - $nx1 - 1) * (3 * $ny2 + 2) + 2 * ($iq - $ny1) + 1
        $num[1] = $num[0] - 1
        $num[2] = $num[0] - 2
        $num[3] = $nc + ($ip - $nx1 - 1) * (3 * $ny2 + 2) + 2 * $ny2 + $iq - $ny1 + 1
        $num[4] = $nc + ($ip - $nx1) * (3 * $ny2 + 2) + 2 * ($iq - $ny1) - 1
        $num[5] = $num[4] + 1
        $num[6] = $num[4] + 2
        $num[7] = $num[3] + 1

        $coord[0,0] = $w1 + $s1 + ($ip - $nx1 - 1) * $frh
        $coord[0,2] = $coord[0,0]
        $coord[0,4] = $w1 + $s1 + ($ip - $nx1) * $frh
        $coord[0,6] = $coord[0,4]
        $coord[1,0] = -$h1 - ($iq - $ny1) * $facs
        $coord[1,2] = -$h1 - ($iq - $ny1 - 1) * $facs
        $coord[1,4] = -$h1 - ($iq - $ny1 - 1) * $facs
        $coord[1,6] = -$h1 - ($iq - $ny1) * $facs
    }

    $coord[0, 1] = 0.5 * ($coord[0, 0] + $coord[0, 2])
    $coord[1, 1] = 0.5 * ($coord[1, 0] + $coord[1, 2])
    $coord[0, 3] = 0.5 * ($coord[0, 2] + $coord[0, 4])
    $coord[1, 3] = 0.5 * ($coord[1, 2] + $coord[1, 4])
    $coord[0, 5] = 0.5 * ($coord[0, 4] + $coord[0, 6])
    $coord[1, 5] = 0.5 * ($coord[1, 4] + $coord[1, 6])
    $coord[0, 7] = 0.5 * ($coord[0, 6] + $coord[0, 0])
    $coord[1, 7] = 0.5 * ($coord[1, 6] + $coord[1, 0])

    return @{ Num = $num; Coord = $coord }
}

function Invoke-Emb2dBc {
    param([int]$nx1, [int]$nx2, [int]$ny1, [int]$ny2, [int]$nn)

    $nodof = 2
    $nf = New-Object int[] ($nodof * $nn)
    $nye = $ny1 + $ny2
    $nm = 0
    $ic = 0

    for ($i = 0; $i -lt 2 * $nye; $i++) {
        $nf[0 + $nm * $nodof] = 0
        $nf[1 + $nm * $nodof] = ++$ic
        $nm++
    }
    $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++

    for ($j = 0; $j -lt $nx1 - 1; $j++) {
        for ($i = 0; $i -lt $nye; $i++) {
            $nf[0 + $nm * $nodof] = ++$ic
            $nf[1 + $nm * $nodof] = ++$ic
            $nm++
        }
        $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++
        for ($i = 0; $i -lt 2 * $nye; $i++) {
            $nf[0 + $nm * $nodof] = ++$ic
            $nf[1 + $nm * $nodof] = ++$ic
            $nm++
        }
        $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++
    }

    for ($i = 0; $i -lt $nye; $i++) {
        $nf[0 + $nm * $nodof] = ++$ic
        $nf[1 + $nm * $nodof] = ++$ic
        $nm++
    }
    $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++

    for ($i = 0; $i -lt 2 * $ny1; $i++) {
        $nf[0 + $nm * $nodof] = ++$ic
        $nf[1 + $nm * $nodof] = ++$ic
        $nm++
    }

    if ($nx2 -eq 0) {
        for ($i = 0; $i -lt 2 * $ny2; $i++) {
            $nf[0 + $nm * $nodof] = 0
            $nf[1 + $nm * $nodof] = ++$ic
            $nm++
        }
        $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++
    } else {
        for ($i = 0; $i -lt 2 * $ny2; $i++) {
            $nf[0 + $nm * $nodof] = ++$ic
            $nf[1 + $nm * $nodof] = ++$ic
            $nm++
        }
        $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++
        for ($jj = 0; $jj -lt $nx2; $jj++) {
            for ($i = 0; $i -lt $ny2; $i++) {
                $nf[0 + $nm * $nodof] = ++$ic
                $nf[1 + $nm * $nodof] = ++$ic
                $nm++
            }
            $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++
            for ($i = 0; $i -lt 2 * $ny2; $i++) {
                $fixX = if ($jj -eq $nx2 - 1) { 0 } else { ++$ic }
                $nf[0 + $nm * $nodof] = $fixX
                $nf[1 + $nm * $nodof] = ++$ic
                $nm++
            }
            $nf[0 + $nm * $nodof] = 0; $nf[1 + $nm * $nodof] = 0; $nm++
        }
    }

    if ($nm -ne $nn) {
        throw "emb_2d_bc node count mismatch: built $nm expected $nn"
    }
    return $nf
}

# --- build Q8 mesh and split to T3 ---
$nye = $Ny1 + $Ny2
$nQ8 = $Nx1 * $nye + $Ny2 * $Nx2
$nn  = (3 * $nye + 2) * $Nx1 + 2 * $nye + 1 + (3 * $Ny2 + 2) * $Nx2

$nodeX = New-Object 'System.Collections.Generic.Dictionary[int,double]'
$nodeY = New-Object 'System.Collections.Generic.Dictionary[int,double]'
$t3 = New-Object 'System.Collections.Generic.List[object]'

for ($iel = 1; $iel -le $nQ8; $iel++) {
    $g = Invoke-Emb2dGeom -iel $iel -nx1 $Nx1 -nx2 $Nx2 -ny1 $Ny1 -ny2 $Ny2 `
        -w1 $w1 -s1 $s1 -w2 $w2 -h1 $h1 -h2 $h2
    $num = $g.Num
    $coord = $g.Coord
    # Export Q8 corner nodes only (T3 split); use GetValue — PowerShell [0,$k] on double[,]
    # is unreliable for midside indices and writes (0,0) if all eight are exported.
    foreach ($k in @(0, 2, 4, 6)) {
        $nid = $num[$k]
        $x = $coord.GetValue(0, $k)
        $y = $coord.GetValue(1, $k)   # Smith-Griffiths: crest y=0, base y negative
        $nodeX[$nid] = $x
        $nodeY[$nid] = $y + $h1 + $h2  # GUI y-up: base at 0, crest at h1+h2
    }
    $n0 = $num[0]; $n2 = $num[2]; $n4 = $num[4]; $n6 = $num[6]
  # Triangle split (same as emb_2d Q8→T3)
    $t3.Add(@($n0,$n4,$n2)) | Out-Null
    $t3.Add(@($n0,$n6,$n4)) | Out-Null
}

$nT3 = $t3.Count

# Remap sparse emb_2d node labels to contiguous 1..nn for p610 .dat.
$oldIds = @($nodeX.Keys | Sort-Object)
$nnDense = $oldIds.Count
$idMap = @{}
$denseCoord = @{}
for ($i = 0; $i -lt $oldIds.Count; $i++) {
    $oid = $oldIds[$i]
    $nid = $i + 1
    $idMap[$oid] = $nid
    $denseCoord[$nid] = @{ X = $nodeX[$oid]; Y = $nodeY[$oid] }
}

$t3Dense = New-Object 'System.Collections.Generic.List[object]'
foreach ($tri in $t3) {
    $t3Dense.Add(@(
        $idMap[$tri[0]],
        $idMap[$tri[1]],
        $idMap[$tri[2]]
    )) | Out-Null
}

# BCs on dense T3 mesh (y-up: base y≈0, crest y≈h1+h2). Matches SLOPE64 p64 roller/pin layout.
# Left roller ux=0; right roller ux=0; base uy=0 (corners pinned).
$tol = 0.51
$xmax = ($denseCoord.Values | ForEach-Object { $_.X } | Measure-Object -Maximum).Maximum
$ymax = ($denseCoord.Values | ForEach-Object { $_.Y } | Measure-Object -Maximum).Maximum
$ymin = ($denseCoord.Values | ForEach-Object { $_.Y } | Measure-Object -Minimum).Minimum

$bcRows = New-Object 'System.Collections.Generic.List[object]'
foreach ($nid in 1..$nnDense) {
    $c = $denseCoord[$nid]
    $fx = 1; $fy = 1
    if ($c.X -le $tol) { $fx = 0 }
    if ($c.X -ge $xmax - $tol) { $fx = 0 }
    if ($c.Y -le $ymin + $tol) {
        $fy = 0
        if ($c.X -le $tol -or $c.X -ge $xmax - $tol) { $fx = 0 }
    }
    if ($fx -eq 0 -or $fy -eq 0) {
        $bcRows.Add([pscustomobject]@{ Node = $nid; Fx = $fx; Fy = $fy }) | Out-Null
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('! <solver:p610_t3>')
[void]$sb.AppendLine('! SLOPE64 Example 1: homogeneous embankment (full structured T3 mesh).')
[void]$sb.AppendLine('! Generated by tools/gen_slope64_ex1.ps1')
[void]$sb.AppendLine("! SLOPE64 counts: nx1=$Nx1 nx2=$Nx2 ny1=$Ny1 ny2=$Ny2  ->  $nQ8 Q8 / $nT3 T3 triangles, $nnDense nodes (dense 1..nn)")
[void]$sb.AppendLine('! Geometry (m): w1=12 s1=20 w2=12 h1=10 h2=10  |  y-up: base y=0, crest y=20')
[void]$sb.AppendLine('! Soil: phi=30 c=5 psi=0 gamma=20 E=1e5 nu=0.3')
[void]$sb.AppendLine("! Strength reduction trial: srf_F=$SrfF (tune toward SLOPE64 failure mechanism)")
[void]$sb.AppendLine('! <BEGIN:AnalysisOptions>')
[void]$sb.AppendLine('! failure_criterion MohrCoulomb')
[void]$sb.AppendLine('! material_type Isotropic')
[void]$sb.AppendLine('! mesh_type T3')
[void]$sb.AppendLine('! solver_executable slope_stability_T3_SRF')
[void]$sb.AppendLine("! srf_F $SrfF")
[void]$sb.AppendLine('! <END:AnalysisOptions>')
[void]$sb.AppendLine('! Geostatic stress: legacy K0*gamma*depth (free surface auto at ymax for y-up mesh).')
[void]$sb.AppendLine("$nT3  $nnDense  1")
[void]$sb.AppendLine('30  5  0  20  100000  0.3  0.5')

foreach ($oldId in $oldIds) {
    $x = $nodeX[$oldId]
    $y = $nodeY[$oldId]   # already y-up in nodeY dictionary
    [void]$sb.AppendLine(("{0:G10}  {1:G10}" -f $x, $y))
}

foreach ($tri in $t3Dense) {
    [void]$sb.AppendLine(("{0}  {1}  {2}" -f $tri[0], $tri[1], $tri[2]))
}

[void]$sb.AppendLine($bcRows.Count)
foreach ($bc in $bcRows) {
    [void]$sb.AppendLine(("{0}  {1}  {2}" -f $bc.Node, $bc.Fx, $bc.Fy))
}

$outSample = [Math]::Min(40, $nnDense)
[void]$sb.AppendLine($outSample)
[void]$sb.AppendLine(([string]::Join('  ', (1..$outSample))))

[void]$sb.AppendLine('0.02  1000  20  1')
[void]$sb.AppendLine('0')

$dir = Split-Path -Parent $OutPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($OutPath, $sb.ToString().TrimEnd() + "`n")
Write-Host "Wrote $OutPath  ($nT3 T3, $nnDense nodes, $($bcRows.Count) BC rows)"
