<#
.SYNOPSIS
  Generate Smith & Griffiths Example 2 (two-layer slope) explicit p64 Q8 SRF .dat.

.DESCRIPTION
  Ports emb_2d_geom / emb_2d_bc (Program 6.4) with two Mohr-Coulomb materials and
  layered foundation etype assignment. Writes:
    Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/gen_slope64_ex2.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $OutPath,
    [double] $W1 = 1.2,
    [double] $S1 = 2.0,
    [double] $W2 = 1.2,
    [double] $H1 = 1.0,
    [double] $H2 = 1.0,
    [int]    $Nx1 = 5,
    [int]    $Nx2 = 5,
    [int]    $Ny1 = 5,
    [int]    $Ny2 = 5,
    [double] $Phi1Deg = 25.0,
    [double] $C1_kPa = 1.0,
    [double] $Phi2Deg = 15.0,
    [double] $C2_kPa = 0.5,
    [double] $PsiDeg = 0.0,
    [double] $Gamma = 20.0,
    [double] $E_kPa = 1.0e5,
    [double] $Nu = 0.3,
    [double] $Tol = 0.0001,
    [int]    $Limit = 500,
    [double[]] $SrfMulti = @(1.0, 1.1, 1.2, 1.3, 1.4, 1.5)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
if (-not $OutPath) {
    $OutPath = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope\ex2_twolayer_slope.dat'
}

function Format-Double {
    param($Value)
    return ([double] $Value).ToString('G10', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-Ex2ElementType {
    param([int]$iel, [int]$nx1, [int]$nx2, [int]$ny1, [int]$ny2)
    $nt = $nx1 * $ny1
    if ($iel -le $nt) { return 1 }
    $nxe = $nx1 + $nx2
    $iq = [int][math]::Floor(($iel - $nt - 1) / $nxe) + $ny1 + 1
    $fr = $iq - $ny1
    if ($fr -le 3) { return 2 } else { return 1 }
}

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

function Build-Q8Mesh {
    param(
        [int]$Nx1, [int]$Nx2, [int]$Ny1, [int]$Ny2,
        [double]$W1, [double]$S1, [double]$W2, [double]$H1, [double]$H2
    )

    $nye = $Ny1 + $Ny2
    $nQ8 = $Nx1 * $nye + $Ny2 * $Nx2
    $nn  = (3 * $nye + 2) * $Nx1 + 2 * $nye + 1 + (3 * $Ny2 + 2) * $Nx2

    function Resolve-NodeCoord {
        param($List)
        $leftTol = 1e-9
        $onLeft = @($List | Where-Object { [math]::Abs($_.X) -le $leftTol })
        if ($onLeft.Count -gt 0) {
            $mids = @($onLeft | Where-Object { -not $_.IsCorner })
            $pool = if ($mids.Count -gt 0) { $mids } else { $onLeft }
        } else {
            $mids = @($List | Where-Object { -not $_.IsCorner })
            $pool = if ($mids.Count -gt 0) { $mids } else { $List }
        }
        $pool = @($pool)
        if ($pool.Count -eq 0) { return [pscustomobject]@{ X = 0.0; Y = 0.0 } }
        $sx = ($pool | Measure-Object -Property X -Sum).Sum
        $sy = ($pool | Measure-Object -Property Y -Sum).Sum
        $n = $pool.Count
        return [pscustomobject]@{ X = $sx / $n; Y = $sy / $n }
    }

    $elems = New-Object 'System.Collections.Generic.List[object]'
    $etypes = New-Object 'System.Collections.Generic.List[int]'
    $samples = New-Object 'System.Collections.Generic.Dictionary[int,System.Collections.Generic.List[object]]'

    for ($iel = 1; $iel -le $nQ8; $iel++) {
        $g = Invoke-Emb2dGeom -iel $iel -nx1 $Nx1 -nx2 $Nx2 -ny1 $Ny1 -ny2 $Ny2 `
            -w1 $W1 -s1 $S1 -w2 $W2 -h1 $H1 -h2 $H2
        $num = $g.Num
        $coord = $g.Coord
        $conn = @($num[0], $num[1], $num[2], $num[3], $num[4], $num[5], $num[6], $num[7])
        $elems.Add($conn) | Out-Null
        $etypes.Add((Get-Ex2ElementType -iel $iel -nx1 $Nx1 -nx2 $Nx2 -ny1 $Ny1 -ny2 $Ny2)) | Out-Null
        foreach ($k in 0..7) {
            $nid = $conn[$k]
            $j = [Array]::IndexOf($num, $nid)
            if ($j -lt 0) { continue }
            $x = [double]$coord.GetValue(0, $j)
            $y = [double]$coord.GetValue(1, $j)
            if (-not $samples.ContainsKey($nid)) {
                $samples[$nid] = New-Object 'System.Collections.Generic.List[object]'
            }
            $isCorner = ($j -eq 0 -or $j -eq 2 -or $j -eq 4 -or $j -eq 6)
            $samples[$nid].Add([pscustomobject]@{ X = $x; Y = $y; IsCorner = $isCorner }) | Out-Null
        }
    }

    $nodeX = New-Object 'System.Collections.Generic.Dictionary[int,double]'
    $nodeY = New-Object 'System.Collections.Generic.Dictionary[int,double]'
    foreach ($nid in $samples.Keys) {
        $best = Resolve-NodeCoord -List $samples[$nid]
        $nodeX[$nid] = $best.X
        $nodeY[$nid] = $best.Y
    }

    $maxId = ($nodeX.Keys | Measure-Object -Maximum).Maximum
    if ($nodeX.Count -ne $nn -or $maxId -ne $nn) {
        throw "Expected contiguous global node ids 1..nn (got $($nodeX.Count) nodes, max id $maxId, nn=$nn)"
    }

    $coordById = @{}
    for ($nid = 1; $nid -le $nn; $nid++) {
        $coordById[$nid] = @{ X = $nodeX[$nid]; Y = $nodeY[$nid] }
    }

    $nfRaw = Invoke-Emb2dBc -nx1 $Nx1 -nx2 $Nx2 -ny1 $Ny1 -ny2 $Ny2 -nn $nn
    $bcRows = New-Object 'System.Collections.Generic.List[object]'
    for ($nid = 1; $nid -le $nn; $nid++) {
        $ix = ($nid - 1) * 2
        $fx = if ($nfRaw[$ix] -eq 0) { 0 } else { 1 }
        $fy = if ($nfRaw[$ix + 1] -eq 0) { 0 } else { 1 }
        if ($fx -eq 0 -or $fy -eq 0) {
            $bcRows.Add([pscustomobject]@{ Node = $nid; Fx = $fx; Fy = $fy }) | Out-Null
        }
    }

    return @{
        Nels = $nQ8
        Nn = $nn
        DenseCoord = $coordById
        Elems = $elems
        Etypes = $etypes
        BcRows = $bcRows
    }
}

function Set-MeshPositiveCoordinates {
    param([hashtable] $Mesh)
    $yMin = ($Mesh.DenseCoord.Values | ForEach-Object { $_.Y } | Measure-Object -Minimum).Minimum
    $offset = 0.0
    if ($yMin -lt 0) {
        $offset = -$yMin
        foreach ($nid in @($Mesh.DenseCoord.Keys)) {
            $Mesh.DenseCoord[$nid].Y += $offset
        }
    }
    return $offset
}

function Write-Ex2P64Dat {
    param(
        [string] $Path,
        [hashtable] $Mesh,
        [double[]] $SrfList,
        [double] $Tol,
        [int] $Limit,
        [double] $MeshCoordYOffset
    )

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('! <solver:p64>')
    [void]$sb.AppendLine('! Example 2: A two-layer slope (Smith & Griffiths Program 6.4)')
    [void]$sb.AppendLine('! Generated by tools/gen_slope64_ex2.ps1')
    [void]$sb.AppendLine('! Book: k_h=0 (pseudo-static not used by p64 C++); nosurf=0; gam_w=9.81')
    [void]$sb.AppendLine('! Book fos_tol=0.05 is FoS bracket metadata — solver uses displacement tol below')
    [void]$sb.AppendLine('! <BEGIN:AnalysisOptions>')
    [void]$sb.AppendLine('! failure_criterion MohrCoulomb')
    [void]$sb.AppendLine('! material_type Isotropic')
    [void]$sb.AppendLine('! mesh_type Q8')
    [void]$sb.AppendLine('! mesh_source explicit')
    [void]$sb.AppendLine('! solver_executable SRF')
    [void]$sb.AppendLine("! embankment_nx1 $Nx1")
    [void]$sb.AppendLine("! embankment_nx2 $Nx2")
    [void]$sb.AppendLine("! embankment_ny1 $Ny1")
    [void]$sb.AppendLine("! embankment_ny2 $Ny2")
    [void]$sb.AppendLine("! embankment_w1 $(Format-Double $W1)")
    [void]$sb.AppendLine("! embankment_s1 $(Format-Double $S1)")
    [void]$sb.AppendLine("! embankment_w2 $(Format-Double $W2)")
    [void]$sb.AppendLine("! embankment_h1 $(Format-Double $H1)")
    [void]$sb.AppendLine("! embankment_h2 $(Format-Double $H2)")
    [void]$sb.AppendLine("! mesh_coord_y_offset $(Format-Double $MeshCoordYOffset)")
    [void]$sb.AppendLine('! srf_mode multi')
    [void]$sb.AppendLine('! <END:AnalysisOptions>')

    [void]$sb.AppendLine("$($Mesh.Nels)  $($Mesh.Nn)  2")
    [void]$sb.AppendLine("$(Format-Double $Phi1Deg)  $(Format-Double $C1_kPa)  $(Format-Double $PsiDeg)  $(Format-Double $Gamma)  $(Format-Double $E_kPa)  $(Format-Double $Nu)")
    [void]$sb.AppendLine("$(Format-Double $Phi2Deg)  $(Format-Double $C2_kPa)  $(Format-Double $PsiDeg)  $(Format-Double $Gamma)  $(Format-Double $E_kPa)  $(Format-Double $Nu)")

    $etypeLine = ($Mesh.Etypes | ForEach-Object { $_.ToString() }) -join '  '
    [void]$sb.AppendLine($etypeLine)

    for ($nid = 1; $nid -le $Mesh.Nn; $nid++) {
        $c = $Mesh.DenseCoord[$nid]
        [void]$sb.AppendLine("$(Format-Double $c.X)  $(Format-Double $c.Y)")
    }

    foreach ($e in $Mesh.Elems) {
        [void]$sb.AppendLine(("{0}  {1}  {2}  {3}  {4}  {5}  {6}  {7}" -f `
            $e[0], $e[1], $e[2], $e[3], $e[4], $e[5], $e[6], $e[7]))
    }

    [void]$sb.AppendLine($Mesh.BcRows.Count)
    foreach ($bc in $Mesh.BcRows) {
        [void]$sb.AppendLine(("{0}  {1}  {2}" -f $bc.Node, $bc.Fx, $bc.Fy))
    }

    [void]$sb.AppendLine("$(Format-Double $Tol)  $Limit")
    [void]$sb.AppendLine($SrfList.Count)
    [void]$sb.AppendLine(($SrfList | ForEach-Object { Format-Double $_ }) -join '  ')

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $sb.ToString().TrimEnd() + "`n")
    Write-Host "Wrote $Path  ($($Mesh.Nels) Q8, $($Mesh.Nn) nodes, np_types=2, srf_mode=multi)"
}

$mesh = Build-Q8Mesh -Nx1 $Nx1 -Nx2 $Nx2 -Ny1 $Ny1 -Ny2 $Ny2 -W1 $W1 -S1 $S1 -W2 $W2 -H1 $H1 -H2 $H2
$yOffset = Set-MeshPositiveCoordinates -Mesh $mesh
if ($yOffset -le 0) { $yOffset = $H1 + $H2 }

Write-Ex2P64Dat -Path $OutPath -Mesh $mesh -SrfList $SrfMulti -Tol $Tol -Limit $Limit -MeshCoordYOffset $yOffset

# Sanity: etype counts
$t1 = @($mesh.Etypes | Where-Object { $_ -eq 1 }).Count
$t2 = @($mesh.Etypes | Where-Object { $_ -eq 2 }).Count
Write-Host "Element types: soil1=$t1 soil2=$t2 (expected 45 and 30)"
