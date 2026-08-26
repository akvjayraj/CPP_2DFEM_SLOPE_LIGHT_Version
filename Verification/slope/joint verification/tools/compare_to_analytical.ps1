$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$jr01 = Join-Path $root "JR01_SingleJointNormalCompression\decks"
$jr02 = Join-Path $root "JR02_SingleJoint45DegCompression\decks"
$jr03 = Join-Path $root "JR03_SingleJointColumnAxial\decks"
$jr04 = Join-Path $root "JR04_SingleJointColumnBodyForce\decks"
$jr05 = Join-Path $root "JR05_TriaxialJointSlip\decks"
$outPath = Join-Path $root "joint_verification_results.txt"
$exe = "C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\library\solver\p610_t3_elastoplastic.exe"
$kn = 1.0e5
$ks = 1.0e5
$E = 3.0e7
$nu = 0.25

function Read-Nums([string]$text, [string]$key) {
    $list = New-Object System.Collections.Generic.List[double]
    foreach ($m in [regex]::Matches($text, '"' + $key + '"\s*:\s*([0-9eE.+-]+)')) {
        $list.Add([double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture))
    }
    return $list
}

function Read-JointJson([string]$path) {
    $text = [System.IO.File]::ReadAllText($path)
    $c = Read-Nums $text 'closure'
    if ($c.Count -eq 0) { throw "no joints in $path" }
    $sn = Read-Nums $text 'sn_kPa'
    $tau = Read-Nums $text 'tau_kPa'
    $un = Read-Nums $text 'un'
    $us = Read-Nums $text 'us'
    $L = Read-Nums $text 'L'
    $n = $c.Count
    $sumC = 0.0; $minC = [double]::MaxValue; $maxC = [double]::MinValue
    $sumSn = 0.0; $sumTau = 0.0; $sumUn = 0.0; $sumUs = 0.0; $sumL = 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $sumC += $c[$i]; if ($c[$i] -lt $minC) { $minC = $c[$i] }; if ($c[$i] -gt $maxC) { $maxC = $c[$i] }
        $sumSn += $sn[$i]; $sumTau += $tau[$i]; $sumUn += $un[$i]; $sumUs += $us[$i]; $sumL += $L[$i]
    }
    $slipped = ([regex]::Matches($text, '"slipped"\s*:\s*true')).Count
    $fyM = [regex]::Match($text, '"fy"\s*:\s*([0-9eE.+-]+)')
    $fy = if ($fyM.Success) { [double]::Parse($fyM.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { 0.0 }
    return [pscustomobject]@{
        N = $n
        AvgClosure = $sumC / $n
        MinClosure = $minC
        MaxClosure = $maxC
        AvgSn = $sumSn / $n
        AvgTau = $sumTau / $n
        AvgUn = $sumUn / $n
        AvgUs = $sumUs / $n
        Length = $sumL
        Slipped = $slipped
        Fy = $fy
    }
}

function PctErr([double]$num, [double]$an) {
    if ([math]::Abs($an) -lt 1e-15) {
        if ([math]::Abs($num) -lt 1e-8) { return 0.0 }
        return [double]::PositiveInfinity
    }
    return 100.0 * [math]::Abs($num - $an) / [math]::Abs($an)
}

function Status([double]$errPct, [double]$tol = 1.0) {
    if ([double]::IsNaN($errPct) -or [double]::IsInfinity($errPct)) { return "FAIL" }
    if ($errPct -lt $tol) { return "PASS" }
    return "FAIL"
}

function Write-Jr02Dat([string]$path, [double]$syy, [double]$phi, [int]$incs = 5) {
    $nx = 8; $ny = 16; $nxl = $nx + 1; $nyl = $ny + 1
    $dx = 1.0 / $nx; $dy = 2.0 / $ny
    $jointOff = 4
    $lower = @{}
    $upper = @{}
    $coords = New-Object System.Collections.Generic.List[object]
    $id = 1
    for ($iy = 0; $iy -lt $nyl; $iy++) {
        for ($ix = 0; $ix -lt $nxl; $ix++) {
            if ($iy -le ($ix + $jointOff)) {
                $lower["$ix,$iy"] = $id
                $coords.Add([pscustomobject]@{ x = $ix * $dx; y = $iy * $dy })
                $id++
            }
        }
    }
    for ($iy = 0; $iy -lt $nyl; $iy++) {
        for ($ix = 0; $ix -lt $nxl; $ix++) {
            if ($iy -ge ($ix + $jointOff)) {
                $upper["$ix,$iy"] = $id
                $coords.Add([pscustomobject]@{ x = $ix * $dx; y = $iy * $dy })
                $id++
            }
        }
    }
    $nn = $id - 1
    function NL([int]$ix, [int]$iy) { return $lower["$ix,$iy"] }
    function NU([int]$ix, [int]$iy) { return $upper["$ix,$iy"] }

    $tris = New-Object System.Collections.Generic.List[string]
    for ($iy = 0; $iy -lt $ny; $iy++) {
        for ($ix = 0; $ix -lt $nx; $ix++) {
            if ($iy -lt ($ix + $jointOff)) {
                $n00 = NL $ix $iy; $n10 = NL ($ix + 1) $iy
                $n11 = NL ($ix + 1) ($iy + 1); $n01 = NL $ix ($iy + 1)
                $tris.Add("$n00  $n10  $n11")
                $tris.Add("$n00  $n11  $n01")
            }
            elseif ($iy -gt ($ix + $jointOff)) {
                $n00 = NU $ix $iy; $n10 = NU ($ix + 1) $iy
                $n11 = NU ($ix + 1) ($iy + 1); $n01 = NU $ix ($iy + 1)
                $tris.Add("$n00  $n10  $n11")
                $tris.Add("$n00  $n11  $n01")
            }
            else {
                $tris.Add("$(NL $ix $iy)  $(NL ($ix+1) $iy)  $(NL ($ix+1) ($iy+1))")
                $tris.Add("$(NU $ix $iy)  $(NU ($ix+1) ($iy+1))  $(NU $ix ($iy+1))")
            }
        }
    }
    $nels = $tris.Count

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("! <solver:p610_t3>")
    [void]$sb.AppendLine("! JR-02 45-deg joint  syy=$syy kPa  phi=$phi")
    [void]$sb.AppendLine("! <BEGIN:AnalysisOptions>")
    [void]$sb.AppendLine("! failure_criterion Elastic")
    [void]$sb.AppendLine("! material_type Isotropic")
    [void]$sb.AppendLine("! mesh_type T3")
    [void]$sb.AppendLine("! solver_executable p610_t3_elastoplastic")
    [void]$sb.AppendLine("! <END:AnalysisOptions>")
    [void]$sb.AppendLine("$nels  $nn  1")
    [void]$sb.AppendLine("89  1.0e10  0  0  30000000  0.25  0")
    foreach ($p in $coords) {
        [void]$sb.AppendLine("$($p.x.ToString('G10',[Globalization.CultureInfo]::InvariantCulture))  $($p.y.ToString('G10',[Globalization.CultureInfo]::InvariantCulture))")
    }
    foreach ($t in $tris) { [void]$sb.AppendLine($t) }

    [void]$sb.AppendLine("$nxl")
    for ($ix = 0; $ix -lt $nxl; $ix++) {
        $nid = NL $ix 0
        if ($ix -eq 0) { [void]$sb.AppendLine("$nid  0  0") } else { [void]$sb.AppendLine("$nid  1  0") }
    }

    $watch = @(
        (NL 0 0), (NL $nx 0),
        (NL 0 $jointOff), (NL $nx ($nx + $jointOff)),
        (NU 0 $jointOff), (NU $nx ($nx + $jointOff)),
        (NU 0 $ny), (NU $nx $ny)
    )
    [void]$sb.AppendLine("$($watch.Count)")
    [void]$sb.AppendLine(($watch -join "  "))
    [void]$sb.AppendLine("0.0001  80  $incs  0")

    if ([math]::Abs($syy) -lt 1e-15) {
        [void]$sb.AppendLine("0")
    } else {
        [void]$sb.AppendLine("1")
        [void]$sb.AppendLine("0  0  $($syy.ToString('G10',[Globalization.CultureInfo]::InvariantCulture))  0")
        [void]$sb.AppendLine("$nx")
        for ($ix = $nx - 1; $ix -ge 0; $ix--) {
            [void]$sb.AppendLine("$(NU ($ix+1) $ny)  $(NU $ix $ny)")
        }
    }

    $phiTok = $phi.ToString("G10", [Globalization.CultureInfo]::InvariantCulture)
    [void]$sb.AppendLine("! <BEGIN:Joints>")
    [void]$sb.AppendLine("! include true")
    [void]$sb.AppendLine("! njoint 1")
    [void]$sb.AppendLine("! ninterface $nx")
    [void]$sb.AppendLine("! joint weakplane `"JR-02 joint 45 deg`"")
    [void]$sb.AppendLine("!   kn 100000")
    [void]$sb.AppendLine("!   ks 100000")
    [void]$sb.AppendLine("!   cohesion 0")
    [void]$sb.AppendLine("!   friction $phiTok")
    [void]$sb.AppendLine("!   tension 0")
    [void]$sb.AppendLine("!   ends both_open")
    [void]$sb.AppendLine("!   install_stage 1")
    [void]$sb.AppendLine("!   nvert 2")
    [void]$sb.AppendLine("!     0  0.5")
    [void]$sb.AppendLine("!     1  1.5")
    [void]$sb.AppendLine("! interfaces")
    for ($ix = 0; $ix -lt $nx; $ix++) {
        $n1 = NL $ix ($ix + $jointOff)
        $n2 = NL ($ix + 1) ($ix + 1 + $jointOff)
        $n3 = NU ($ix + 1) ($ix + 1 + $jointOff)
        $n4 = NU $ix ($ix + $jointOff)
        [void]$sb.AppendLine("!   $n1 $n2 $n3 $n4  100000 100000 0 $phiTok 0  both_open 1 0 0")
    }
    [void]$sb.AppendLine("! <END:Joints>")
    [System.IO.File]::WriteAllText($path, $sb.ToString())
}

function Write-Jr03Dat([string]$path, [double]$syy, [int]$nx, [int]$ny, [int]$incs = 5) {
    $nxl = $nx + 1
    $nyL = [int]($ny / 2)
    $nyU = $ny - $nyL
    $H = 3.0
    $yj = 1.5
    $E = 2.0e6
    $nu = 0.01
    $kn = 1.0e7
    $phi = 89.0
    $nLower = $nxl * ($nyL + 1)
    $nUpper = $nxl * ($nyU + 1)
    $nn = $nLower + $nUpper
    $nels = $nx * $nyL * 2 + $nx * $nyU * 2
    function NL([int]$ix, [int]$iy) { return $iy * $nxl + $ix + 1 }
    function NU([int]$ix, [int]$iy) { return $nLower + $iy * $nxl + $ix + 1 }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("! <solver:p610_t3>")
    [void]$sb.AppendLine("! JR-03 RS3 Case 1  syy=$syy kPa  nx=$nx ny=$ny")
    [void]$sb.AppendLine("! <BEGIN:AnalysisOptions>")
    [void]$sb.AppendLine("! failure_criterion Elastic")
    [void]$sb.AppendLine("! material_type Isotropic")
    [void]$sb.AppendLine("! mesh_type T3")
    [void]$sb.AppendLine("! solver_executable p610_t3_elastoplastic")
    [void]$sb.AppendLine("! <END:AnalysisOptions>")
    [void]$sb.AppendLine("$nels  $nn  1")
    [void]$sb.AppendLine("89  1.0e10  0  0  2000000  0.01  0")
    $dx = 1.0 / $nx
    for ($iy = 0; $iy -le $nyL; $iy++) {
        $y = $yj * $iy / [double]$nyL
        for ($ix = 0; $ix -le $nx; $ix++) {
            [void]$sb.AppendLine("$($ix * $dx)  $($y.ToString('G10',[Globalization.CultureInfo]::InvariantCulture))")
        }
    }
    for ($iy = 0; $iy -le $nyU; $iy++) {
        $y = $yj + ($H - $yj) * $iy / [double]$nyU
        for ($ix = 0; $ix -le $nx; $ix++) {
            [void]$sb.AppendLine("$($ix * $dx)  $($y.ToString('G10',[Globalization.CultureInfo]::InvariantCulture))")
        }
    }
    for ($iy = 0; $iy -lt $nyL; $iy++) {
        for ($ix = 0; $ix -lt $nx; $ix++) {
            $n00 = NL $ix $iy; $n10 = NL ($ix + 1) $iy
            $n11 = NL ($ix + 1) ($iy + 1); $n01 = NL $ix ($iy + 1)
            [void]$sb.AppendLine("$n00  $n10  $n11")
            [void]$sb.AppendLine("$n00  $n11  $n01")
        }
    }
    for ($iy = 0; $iy -lt $nyU; $iy++) {
        for ($ix = 0; $ix -lt $nx; $ix++) {
            $n00 = NU $ix $iy; $n10 = NU ($ix + 1) $iy
            $n11 = NU ($ix + 1) ($iy + 1); $n01 = NU $ix ($iy + 1)
            [void]$sb.AppendLine("$n00  $n10  $n11")
            [void]$sb.AppendLine("$n00  $n11  $n01")
        }
    }
    [void]$sb.AppendLine("$nxl")
    for ($ix = 0; $ix -le $nx; $ix++) {
        $nid = NL $ix 0
        if ($ix -eq 0) { [void]$sb.AppendLine("$nid  0  0") } else { [void]$sb.AppendLine("$nid  1  0") }
    }
    $watch = @((NL 0 0), (NL $nx 0), (NL 0 $nyL), (NL $nx $nyL), (NU 0 0), (NU $nx 0), (NU 0 $nyU), (NU $nx $nyU))
    [void]$sb.AppendLine("$($watch.Count)")
    [void]$sb.AppendLine(($watch -join "  "))
    [void]$sb.AppendLine("0.0001  80  $incs  0")
    if ([math]::Abs($syy) -lt 1e-15) {
        [void]$sb.AppendLine("0")
    } else {
        [void]$sb.AppendLine("1")
        [void]$sb.AppendLine("0  0  $($syy.ToString('G10',[Globalization.CultureInfo]::InvariantCulture))  0")
        [void]$sb.AppendLine("$nx")
        for ($ix = $nx - 1; $ix -ge 0; $ix--) {
            [void]$sb.AppendLine("$(NU ($ix+1) $nyU)  $(NU $ix $nyU)")
        }
    }
    [void]$sb.AppendLine("! <BEGIN:Joints>")
    [void]$sb.AppendLine("! include true")
    [void]$sb.AppendLine("! njoint 1")
    [void]$sb.AppendLine("! ninterface $nx")
    [void]$sb.AppendLine("! joint weakplane `"JR-03 horizontal joint`"")
    [void]$sb.AppendLine("!   kn 10000000")
    [void]$sb.AppendLine("!   ks 10000000")
    [void]$sb.AppendLine("!   cohesion 0")
    [void]$sb.AppendLine("!   friction 89")
    [void]$sb.AppendLine("!   tension 0")
    [void]$sb.AppendLine("!   ends both_open")
    [void]$sb.AppendLine("!   install_stage 1")
    [void]$sb.AppendLine("!   nvert 2")
    [void]$sb.AppendLine("!     0  1.5")
    [void]$sb.AppendLine("!     1  1.5")
    [void]$sb.AppendLine("! interfaces")
    for ($ix = 0; $ix -lt $nx; $ix++) {
        $n1 = NL $ix $nyL
        $n2 = NL ($ix + 1) $nyL
        $n3 = NU ($ix + 1) 0
        $n4 = NU $ix 0
        [void]$sb.AppendLine("!   $n1 $n2 $n3 $n4  10000000 10000000 0 89 0  both_open 1 0 0")
    }
    [void]$sb.AppendLine("! <END:Joints>")
    [System.IO.File]::WriteAllText($path, $sb.ToString())
}

function Write-Jr04Dat([string]$path, [int]$nx, [int]$ny) {
    Write-Jr03Dat $path 0 $nx $ny 5
    $t = [System.IO.File]::ReadAllText($path)
    $t = $t.Replace('JR-03 RS3 Case 1', 'JR-04 RS3 Case 2')
    $t = $t.Replace('89  1.0e10  0  0  2000000  0.01  0', '89  1.0e10  0  10000  2000000  0.01  0')
    $t = $t.Replace('0.0001  80  5  0', '0.0001  80  5  1')
    $t = $t.Replace('JR-03 horizontal joint', 'JR-04 horizontal joint')
    [System.IO.File]::WriteAllText($path, $t)
}

function Jr05-Sigma1([double]$s3, [double]$c, [double]$betaDeg) {
    $beta = $betaDeg * [math]::PI / 180.0
    $phi = 30.0 * [math]::PI / 180.0
    $tanPhi = [math]::Tan($phi)
    $cotB = 1.0 / [math]::Tan($beta)
    $den = (1.0 - $tanPhi * $cotB) * [math]::Sin(2.0 * $beta)
    return $s3 + 2.0 * ($s3 * $tanPhi + $c) / $den
}

function Jr05-Cauchy([double]$s1, [double]$s3, [double]$betaDeg) {
    $b = $betaDeg * [math]::PI / 180.0
    $sn = $s1 * [math]::Cos($b) * [math]::Cos($b) + $s3 * [math]::Sin($b) * [math]::Sin($b)
    $tau = ($s1 - $s3) * [math]::Sin($b) * [math]::Cos($b)
    return [pscustomobject]@{ Sn = $sn; Tau = $tau }
}

function Read-Nodes([string]$text) {
    $cut = $text.IndexOf('"stages"')
    if ($cut -lt 0) { $cut = $text.Length }
    $head = $text.Substring(0, $cut)
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($m in [regex]::Matches($head, '"id"\s*:\s*(\d+)\s*,\s*"x"\s*:\s*([0-9eE.+-]+)\s*,\s*"y"\s*:\s*([0-9eE.+-]+)\s*,\s*"ux"\s*:\s*([0-9eE.+-]+)\s*,\s*"uy"\s*:\s*([0-9eE.+-]+)')) {
        $list.Add([pscustomobject]@{
            X = [double]::Parse($m.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)
            Y = [double]::Parse($m.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)
            Uy = [double]::Parse($m.Groups[5].Value, [Globalization.CultureInfo]::InvariantCulture)
        })
    }
    return $list
}

function UyDownAt($nodes, [double]$x, [double]$y) {
    $hits = @($nodes | Where-Object { [math]::Abs($_.X - $x) -lt 1e-6 -and [math]::Abs($_.Y - $y) -lt 1e-6 })
    if ($hits.Count -eq 0) {
        $n = $nodes | Sort-Object { [math]::Abs($_.X - $x) + [math]::Abs($_.Y - $y) } | Select-Object -First 1
        return -$n.Uy
    }
    if ($hits.Count -ge 2) {
        $downs = @($hits | ForEach-Object { -$_.Uy } | Sort-Object)
        return $downs[-1]
    }
    return -$hits[0].Uy
}

function Run-P610([string]$datPath) {
    $dir = Split-Path $datPath
    $stem = [IO.Path]::GetFileNameWithoutExtension($datPath)
    $p = Start-Process -FilePath $exe -ArgumentList $stem -WorkingDirectory $dir -Wait -PassThru -NoNewWindow
    $json = Join-Path $dir "$stem.json"
    if (-not (Test-Path $json)) { throw "solver produced no JSON for $stem (exit $($p.ExitCode))" }
    return $json
}

$lines = New-Object System.Collections.Generic.List[string]
function AddLine([string]$s) { [void]$lines.Add($s) }

AddLine 'Joint verification - numerical vs analytical'
AddLine ('Generated: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm'))
AddLine 'Solver: p610_t3_elastoplastic  layers=0'
AddLine 'Units: kPa, m.  JR-01/02: kn=1e5 kPa/m, E=3e7 kPa.  JR-03 (RS3 Case 1): kn=1e7 kPa/m, E=2e6 kPa, nu=0.01.'
AddLine 'Sign: un > 0 opening; closure dc = -un; sn compression-positive.'
AddLine 'PASS if relative error on closure/sn/kn is below 1 percent.'
AddLine 'Force equilibrium PASS if relative error on Fy vs sigma*B is below 0.1 percent.'
AddLine ''

# ── JR-01 ────────────────────────────────────────────────────────────────
AddLine '================================================================================'
AddLine 'JR-01  Horizontal joint, normal compression'
AddLine 'Folder: JR01_SingleJointNormalCompression'
AddLine 'Joint: (0,1) -> (1,1).  Analytical: dc = sigma_n / kn,  sn = sigma_v,  tau = 0.'
AddLine '================================================================================'
AddLine ''
AddLine ('{0,-22} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f 'Case','sn_an','sn_num','dc_an','dc_num','err_dc%','err_F%','STAT')

$jr01Cases = @(
    @{ Name = "medium 1 MPa";  File = "jr01_medium_1mpa.json";  Sig = 1000.0 },
    @{ Name = "medium 2 MPa";  File = "jr01_medium_2mpa.json";  Sig = 2000.0 },
    @{ Name = "medium 5 MPa";  File = "jr01_medium_5mpa.json";  Sig = 5000.0 },
    @{ Name = "medium 10 MPa"; File = "jr01_medium_10mpa.json"; Sig = 10000.0 },
    @{ Name = "coarse 10 MPa"; File = "jr01_coarse_10mpa.json"; Sig = 10000.0 },
    @{ Name = "fine 10 MPa";   File = "jr01_fine_10mpa.json";   Sig = 10000.0 }
)

$jr01Pass = 0; $jr01Fail = 0
foreach ($c in $jr01Cases) {
    $r = Read-JointJson (Join-Path $jr01 $c.File)
    $dcAn = $c.Sig / $kn
    $errDc = PctErr $r.AvgClosure $dcAn
    $fAn = $c.Sig * 1.0
    $errF = PctErr ([math]::Abs($r.Fy)) $fAn
    $st = if ((Status $errDc 1.0) -eq "PASS" -and (Status $errF 0.1) -eq "PASS") { "PASS" } else { "FAIL" }
    if ($st -eq "PASS") { $jr01Pass++ } else { $jr01Fail++ }
    AddLine ('{0,-22} {1,10:F1} {2,10:F1} {3,10:F4} {4,10:F4} {5,8:F3} {6,8:F3} {7,6}' -f `
        $c.Name, $c.Sig, $r.AvgSn, $dcAn, $r.AvgClosure, $errDc, $errF, $st)
}

$rLin = @()
foreach ($sig in @(1000.0, 2000.0, 5000.0, 10000.0)) {
    $tag = if ($sig -eq 1000) { "1mpa" } elseif ($sig -eq 2000) { "2mpa" } elseif ($sig -eq 5000) { "5mpa" } else { "10mpa" }
    $r = Read-JointJson (Join-Path $jr01 "jr01_medium_$tag.json")
    $rLin += [pscustomobject]@{ S = $sig; C = $r.AvgClosure }
}
# least-squares kn = sum(S*C)/sum(C*C) through origin, and R^2
$sumSC = 0; $sumCC = 0; $sumS = 0; $sumC = 0; $sumSS = 0
foreach ($p in $rLin) { $sumSC += $p.S * $p.C; $sumCC += $p.C * $p.C; $sumS += $p.S; $sumC += $p.C; $sumSS += $p.S * $p.S }
$kFit = if ($sumCC -gt 0) { $sumSC / $sumCC } else { [double]::NaN }
$meanS = $sumS / $rLin.Count
$ssTot = 0; $ssRes = 0
foreach ($p in $rLin) { $ssTot += ($p.S - $meanS)*($p.S - $meanS); $ssRes += ($p.S - $kFit*$p.C)*($p.S - $kFit*$p.C) }
$r2 = if ($ssTot -gt 0) { 1.0 - $ssRes / $ssTot } else { 1.0 }

AddLine ''
AddLine ('Linearity: k_fit = {0:G6} kPa/m  (analytical kn = {1:G6})  R2 = {2:F6}  {3}' -f `
    $kFit, $kn, $r2, (Status (PctErr $kFit $kn) 1.0))

$r0 = Read-JointJson (Join-Path $jr01 "jr01_medium_unload_0mpa.json")
$rT = Read-JointJson (Join-Path $jr01 "jr01_medium_tension.json")
$unloadStat = if ([math]::Abs($r0.AvgClosure) -lt 1e-6) { 'PASS' } else { 'FAIL' }
AddLine ('Unload 0 MPa: dc_num = {0:G4} m  (analytical 0)  {1}' -f $r0.AvgClosure, $unloadStat)
AddLine ('Tension +100 kPa: un = {0:G4} m  sn = {1:G4} kPa  slipped = {2}/{3}  (expect un>0, sn~0)' -f `
    $rT.AvgUn, $rT.AvgSn, $rT.Slipped, $rT.N)
$tenOk = ($rT.AvgUn -gt 0) -and ([math]::Abs($rT.AvgSn) -lt 1.0)
AddLine ('Tension cutoff: {0}' -f $(if ($tenOk) { 'PASS' } else { 'FAIL' }))
AddLine ('JR-01 summary: {0} PASS, {1} FAIL (load/mesh rows)' -f $jr01Pass, $jr01Fail)
AddLine ''

# ── JR-02 ────────────────────────────────────────────────────────────────
AddLine '================================================================================'
AddLine 'JR-02  Joint at 45 deg to horizontal, vertical compression'
AddLine 'Folder: JR02_SingleJoint45DegCompression'
AddLine 'Joint: (0,0.5) -> (1,1.5).  Uniaxial vertical stress on a 45 deg plane:'
AddLine '  sn = sigma_v / 2'
AddLine '  tau_elastic = sigma_v / 2'
AddLine '  dc = sn / kn = sigma_v / (2 kn)'
AddLine '  us_elastic = tau / ks'
AddLine 'At 10 MPa: sn=5000 kPa, dc=0.050 m, tau=5000 kPa, us=0.050 m.'
AddLine 'The published .xfem has c=0, phi=0. Mohr-Coulomb then cuts tau to 0 and the'
AddLine 'upper block can slide. Two numerical series are reported:'
AddLine '  (A) phi=89 deg  - elastic Goodman check vs the formulae above'
AddLine '  (B) phi=0 deg   - as published in the .xfem (shear strength = 0)'
AddLine '================================================================================'
AddLine ''

New-Item -ItemType Directory -Force -Path $jr02 | Out-Null
$jr02Pass = 0; $jr02Fail = 0

function Run-Jr02Series([double]$phi, [string]$label) {
    AddLine $label
    AddLine ('{0,-22} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f 'Case','sn_an','sn_num','dc_an','dc_num','err_dc%','err_F%','STAT')
    $script:jr02Pass = 0; $script:jr02Fail = 0
    foreach ($sig in @(1000.0, 2000.0, 5000.0, 10000.0)) {
        $tag = 'jr02_medium_{0}mpa_phi{1}' -f [int]($sig/1000), [int]$phi
        $dat = Join-Path $jr02 "$tag.dat"
        Write-Jr02Dat $dat (-$sig) $phi 5
        try {
            $json = Run-P610 $dat
            $r = Read-JointJson $json
            $snAn = $sig / 2.0
            $dcAn = $snAn / $kn
            $errDc = PctErr $r.AvgClosure $dcAn
            $fAn = $sig * 1.0
            $errF = PctErr ([math]::Abs($r.Fy)) $fAn
            $st = if ((Status $errDc 1.0) -eq 'PASS' -and (Status $errF 0.1) -eq 'PASS') { 'PASS' } else { 'FAIL' }
            if ($st -eq 'PASS') { $script:jr02Pass++ } else { $script:jr02Fail++ }
            AddLine ('{0,-22} {1,10:F1} {2,10:F1} {3,10:F4} {4,10:F4} {5,8:F3} {6,8:F3} {7,6}' -f `
                ('medium {0} MPa' -f ($sig/1000)), $snAn, $r.AvgSn, $dcAn, $r.AvgClosure, $errDc, $errF, $st)
            AddLine ('    tau_an={0:F1}  tau_num={1:G6}  us_an={2:F4}  us_num={3:G6}  slipped={4}/{5}' -f `
                ($sig/2.0), $r.AvgTau, (($sig/2.0)/$ks), $r.AvgUs, $r.Slipped, $r.N)
        } catch {
            $script:jr02Fail++
            AddLine ('{0,-22} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f `
                ('medium {0} MPa' -f ($sig/1000)), ($sig/2.0), '-', ($sig/2.0/$kn), '-', '-', '-', 'FAIL')
            AddLine ('    solver error: {0}' -f $_.Exception.Message)
        }
    }
    AddLine ''
}

Run-Jr02Series 89.0 '(A) Elastic joint (phi=89 deg, c=0) - compare to sn=sigma/2, dc=sigma/(2 kn)'
$passA = $jr02Pass; $failA = $jr02Fail
Run-Jr02Series 0.0 '(B) Published joint (phi=0, c=0) - MC cuts shear; expect tau~0 and slip'
$passB = $jr02Pass; $failB = $jr02Fail

# ── JR-03 ────────────────────────────────────────────────────────────────
AddLine '================================================================================'
AddLine 'JR-03  RS3 Case 1 - single jointed rock column under axial pressure'
AddLine 'Folder: JR03_SingleJointColumnAxial'
AddLine 'Column 1 m x 3 m, joint at y=1.5 m.  P=1 MPa, E=2000 MPa, kn=10 GPa/m, nu=0.01.'
AddLine 'Analytical (Deb and Das / RS3):'
AddLine '  u(y) = P y / E  +  H(y - yj) * (P / kn)'
AddLine '  dc = P / kn = 0.100 mm'
AddLine '  u(top) = P H / E + P / kn = 1.500 + 0.100 = 1.600 mm'
AddLine '================================================================================'
AddLine ''

New-Item -ItemType Directory -Force -Path $jr03 | Out-Null
$jr03Kn = 1.0e7
$jr03E = 2.0e6
$jr03Pass = 0; $jr03Fail = 0

function Jr03-AnU([double]$y, [double]$sig) {
    $rock = $sig * $y / $jr03E
    if ($y -gt 1.5 - 1e-12) { return $rock + ($sig / $jr03Kn) }
    return $rock
}

$jr03Cases = @(
    @{ Name = 'medium 1 MPa'; Tag = 'jr03_medium_1mpa'; Sig = 1000.0; Nx = 8; Ny = 24 },
    @{ Name = 'medium 2 MPa'; Tag = 'jr03_medium_2mpa'; Sig = 2000.0; Nx = 8; Ny = 24 },
    @{ Name = 'coarse 1 MPa'; Tag = 'jr03_coarse_1mpa'; Sig = 1000.0; Nx = 4; Ny = 12 },
    @{ Name = 'fine 1 MPa';   Tag = 'jr03_fine_1mpa';   Sig = 1000.0; Nx = 16; Ny = 48 }
)

AddLine ('{0,-16} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f 'Case','dc_an_mm','dc_num_mm','uTop_an','uTop_num','err_u%','err_F%','STAT')

foreach ($c in $jr03Cases) {
    $dat = Join-Path $jr03 "$($c.Tag).dat"
    Write-Jr03Dat $dat (-$c.Sig) $c.Nx $c.Ny 5
    try {
        $json = Run-P610 $dat
        $r = Read-JointJson $json
        $text = [System.IO.File]::ReadAllText($json)
        $nodes = Read-Nodes $text
        $dcAn = $c.Sig / $jr03Kn
        $uTopAn = Jr03-AnU 3.0 $c.Sig
        $uTop = UyDownAt $nodes 0.5 3.0
        $errDc = PctErr $r.AvgClosure $dcAn
        $errU = PctErr $uTop $uTopAn
        $errF = PctErr ([math]::Abs($r.Fy)) $c.Sig
        $st = if ((Status $errDc 1.0) -eq 'PASS' -and (Status $errU 1.0) -eq 'PASS' -and (Status $errF 0.1) -eq 'PASS') { 'PASS' } else { 'FAIL' }
        if ($st -eq 'PASS') { $jr03Pass++ } else { $jr03Fail++ }
        AddLine ('{0,-16} {1,10:F4} {2,10:F4} {3,10:F5} {4,10:F5} {5,8:F3} {6,8:F3} {7,6}' -f `
            $c.Name, (1000*$dcAn), (1000*$r.AvgClosure), $uTopAn, $uTop, $errU, $errF, $st)
        if ($c.Tag -eq 'jr03_medium_1mpa') {
            AddLine '  u(y) along x=0.5 (mm downward) vs Deb-Das:'
            foreach ($y in @(0.0, 0.75, 1.5, 2.25, 3.0)) {
                $an = Jr03-AnU $y $c.Sig
                if ([math]::Abs($y - 1.5) -lt 1e-9) {
                    $pair = @($nodes | Where-Object { [math]::Abs($_.X - 0.5) -lt 1e-6 -and [math]::Abs($_.Y - 1.5) -lt 1e-6 })
                    $downs = @($pair | ForEach-Object { -$_.Uy } | Sort-Object)
                    $lo = if ($downs.Count -ge 2) { $downs[0] } else { $an - $dcAn }
                    $hi = if ($downs.Count -ge 2) { $downs[-1] } else { $an }
                    $anLo = $c.Sig * 1.5 / $jr03E
                    $anHi = $anLo + $dcAn
                    $eLo = PctErr $lo $anLo
                    $eHi = PctErr $hi $anHi
                    AddLine ('    y=1.5-  u_an={0:F4} mm  u_num={1:F4} mm  err={2:F3}%' -f (1000*$anLo), (1000*$lo), $eLo)
                    AddLine ('    y=1.5+  u_an={0:F4} mm  u_num={1:F4} mm  err={2:F3}%  (jump {3:F4} mm)' -f (1000*$anHi), (1000*$hi), $eHi, (1000*$dcAn))
                } else {
                    $num = UyDownAt $nodes 0.5 $y
                    $err = PctErr $num $an
                    AddLine ('    y={0,-4}  u_an={1:F4} mm  u_num={2:F4} mm  err={3:F3}%' -f $y, (1000*$an), (1000*$num), $err)
                }
            }
        }
    } catch {
        $jr03Fail++
        AddLine ('{0,-16} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f $c.Name, '-', '-', '-', '-', '-', '-', 'FAIL')
        AddLine ('    solver error: {0}' -f $_.Exception.Message)
    }
}

AddLine ''
AddLine ('JR-03 summary: {0} PASS, {1} FAIL' -f $jr03Pass, $jr03Fail)
AddLine ''

# ── JR-04 ────────────────────────────────────────────────────────────────
AddLine '================================================================================'
AddLine 'JR-04  RS3 Case 2 - jointed column under body force (gamma = 10 MPa/m)'
AddLine 'Folder: JR04_SingleJointColumnBodyForce'
AddLine 'Analytical: u(y) = (gamma/E)(H y - y^2/2) + H(y-yj)*gamma(H-yj)/kn'
AddLine '  dc = 1.50 mm   u(top) = 24.00 mm   sn = 15 MPa'
AddLine '================================================================================'
AddLine ''

New-Item -ItemType Directory -Force -Path $jr04 | Out-Null
$jr04Pass = 0; $jr04Fail = 0
$jr04E = 2.0e6; $jr04Kn = 1.0e7; $jr04G = 1.0e4
AddLine ('{0,-14} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f 'Case','dc_an_mm','dc_num_mm','uTop_an','uTop_num','err_u%','err_F%','STAT')
$jr04Cases = @(
    @{ Name = 'medium'; Tag = 'jr04_medium'; Nx = 8; Ny = 24 },
    @{ Name = 'coarse'; Tag = 'jr04_coarse'; Nx = 4; Ny = 12 },
    @{ Name = 'fine';   Tag = 'jr04_fine';   Nx = 16; Ny = 48 }
)
foreach ($c in $jr04Cases) {
    $dat = Join-Path $jr04 "$($c.Tag).dat"
    Write-Jr04Dat $dat $c.Nx $c.Ny
    try {
        $json = Run-P610 $dat
        $r = Read-JointJson $json
        $nodes = Read-Nodes ([System.IO.File]::ReadAllText($json))
        $dcAn = 0.0015
        $uTopAn = 0.024
        $uTop = UyDownAt $nodes 0.5 3.0
        $errDc = PctErr $r.AvgClosure $dcAn
        $errU = PctErr $uTop $uTopAn
        $errF = PctErr ([math]::Abs($r.Fy)) 30000.0
        $st = if ((Status $errDc 2.0) -eq 'PASS' -and (Status $errU 2.0) -eq 'PASS') { 'PASS' } else { 'FAIL' }
        if ($st -eq 'PASS') { $jr04Pass++ } else { $jr04Fail++ }
        AddLine ('{0,-14} {1,10:F4} {2,10:F4} {3,10:F5} {4,10:F5} {5,8:F3} {6,8:F3} {7,6}' -f `
            $c.Name, (1000*$dcAn), (1000*$r.AvgClosure), $uTopAn, $uTop, $errU, $errF, $st)
    } catch {
        $jr04Fail++
        AddLine ('{0,-14} FAIL  {1}' -f $c.Name, $_.Exception.Message)
    }
}
AddLine ('JR-04 summary: {0} PASS, {1} FAIL' -f $jr04Pass, $jr04Fail)
AddLine ''

# ── JR-05 ────────────────────────────────────────────────────────────────
AddLine '================================================================================'
AddLine 'JR-05  RS3 Case 4 - triaxial jointed column, Mohr-Coulomb slip'
AddLine 'Folder: JR05_TriaxialJointSlip'
AddLine 'sigma1 = sigma3 + 2(sigma3 tan phi + c) / ((1 - tan phi cot beta) sin 2 beta)'
AddLine 'Case 1: sigma3=35 MPa, c=11.135 MPa.  Case 2: sigma3=70 MPa, c=0.2749 MPa.  phi=30 deg.'
AddLine 'Compare sn and |tau| at the analytical failure sigma1 (2 percent).'
AddLine '================================================================================'
AddLine ''

New-Item -ItemType Directory -Force -Path $jr05 | Out-Null
$jr05Pass = 0; $jr05Fail = 0
AddLine ('{0,-16} {1,10} {2,10} {3,10} {4,10} {5,8} {6,8} {7,6}' -f 'Case','s1_MPa','sn_an','sn_num','tau_an','err_sn%','err_t%','STAT')

$jr05Series = @(
    @{ Label = 'c1'; S3 = 35000.0; C = 11135.0 },
    @{ Label = 'c2'; S3 = 70000.0; C = 274.9 }
)
foreach ($ser in $jr05Series) {
    foreach ($beta in @(35.0, 40.0, 45.0, 50.0, 60.0, 70.0)) {
        $s1 = Jr05-Sigma1 $ser.S3 $ser.C $beta
        $ct = Jr05-Cauchy $s1 $ser.S3 $beta
        $tag = 'jr05_{0}_{1}deg' -f $ser.Label, [int]$beta
        $dat = Join-Path $jr05 "$tag.dat"
        $csDat = Join-Path $jr05 $tag
        # Decks are written by Jr05DeckBuilder; write here via a C# helper file if missing.
        if (-not (Test-Path $dat)) {
            AddLine ('{0,-16} FAIL  dat missing - run Jr05 WriteStandardDecks' -f $tag)
            $jr05Fail++
            continue
        }
        try {
            $json = Run-P610 $dat
            $r = Read-JointJson $json
            $errSn = PctErr $r.AvgSn $ct.Sn
            $errT = PctErr ([math]::Abs($r.AvgTau)) $ct.Tau
            $st = if ((Status $errSn 2.0) -eq 'PASS' -and (Status $errT 2.0) -eq 'PASS') { 'PASS' } else { 'FAIL' }
            if ($st -eq 'PASS') { $jr05Pass++ } else { $jr05Fail++ }
            AddLine ('{0,-16} {1,10:F1} {2,10:F1} {3,10:F1} {4,10:F1} {5,8:F3} {6,8:F3} {7,6}' -f `
                $tag, ($s1/1000.0), $ct.Sn, $r.AvgSn, $ct.Tau, $errSn, $errT, $st)
        } catch {
            $jr05Fail++
            AddLine ('{0,-16} FAIL  {1}' -f $tag, $_.Exception.Message)
        }
    }
}
AddLine ('JR-05 summary: {0} PASS, {1} FAIL' -f $jr05Pass, $jr05Fail)
AddLine ''

AddLine '--------------------------------------------------------------------------------'
AddLine ('JR-01: {0} PASS, {1} FAIL' -f $jr01Pass, $jr01Fail)
AddLine ('JR-02 elastic (A): {0} PASS, {1} FAIL' -f $passA, $failA)
AddLine ('JR-02 published (B): {0} PASS, {1} FAIL vs elastic formulae (shear yield expected)' -f $passB, $failB)
AddLine ('JR-03 RS3 Case 1: {0} PASS, {1} FAIL' -f $jr03Pass, $jr03Fail)
AddLine ('JR-04 RS3 Case 2: {0} PASS, {1} FAIL' -f $jr04Pass, $jr04Fail)
AddLine ('JR-05 RS3 Case 4: {0} PASS, {1} FAIL' -f $jr05Pass, $jr05Fail)
AddLine '--------------------------------------------------------------------------------'
AddLine 'Notes'
AddLine '- JR-01 analytical dc = sigma_n/kn is independent of the rock Young modulus.'
AddLine '- JR-02 elastic analytical uses Cauchy projection of uniaxial sigma_v onto a 45 deg plane.'
AddLine '- JR-03 is RS3 Joint Verification Case 1 (Deb and Das). Compare u(y), not only closure.'
AddLine '- JR-04 is RS3 Case 2: body force gamma=10 MPa/m. Compare u(y) and joint overburden (Fy is informational).'
AddLine '- JR-05 is RS3 Case 4 / RS2 Table 27.1 cohesions. Compare sn and tau at analytical sigma1.'
AddLine '- Source JSON: JR01 / JR02 / JR03 / JR04 / JR05 decks/*.json written by this script.'

[System.IO.File]::WriteAllLines($outPath, $lines)
Write-Host "Wrote $outPath"
