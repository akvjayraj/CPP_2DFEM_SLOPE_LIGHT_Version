#requires -Version 5.1
<#
.SYNOPSIS
  Quick diagnostic comparing FE element stresses against the Lame closed-form
  for a generated thick-cylinder dat. Used to tune mesh density / tolerance.
#>
param(
    [string] $DatStem = 'nafems_le10_thick_cyl_t3',
    [double] $a = 1.0,
    [double] $b = 2.0,
    [double] $p = 100.0,
    [double] $nu = 0.25,
    [bool] $RunSolver = $true
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $RepoRoot ("Verification\multi_element\{0}" -f $DatStem)
$exe = Join-Path $RepoRoot 'library\solver\p610_t3_elastoplastic.exe'
if ($RunSolver) {
    Push-Location $dir
    try { & $exe $DatStem | Out-Null } finally { Pop-Location }
}
$json = (Get-Content (Join-Path $dir "$DatStem.json") -Raw) -replace '(?<![\w.])-?nan(?![\w.])','null' -replace '(?<![\w.])-?inf(?![\w.])','null' | ConvertFrom-Json

$nodes = @{}
foreach ($n in $json.nodes) { $nodes[[int]$n.id] = @{ X = [double]$n.x; Y = [double]$n.y } }

# Inner-pressure thick cylinder Lame solution (plane strain, tension-positive):
#   sigma_rr(r) = p*a^2/(b^2-a^2) * (1 - b^2/r^2)
#   sigma_tt(r) = p*a^2/(b^2-a^2) * (1 + b^2/r^2)
$factor = $p * $a * $a / ($b*$b - $a*$a)

$rows = New-Object System.Collections.Generic.List[object]
foreach ($el in $json.stages[-1].element_stresses) {
    $node1 = $json.elements | Where-Object { $_.id -eq $el.id } | Select-Object -First 1
    if (-not $node1) { continue }
    $nIds = @($node1.n1, $node1.n2, $node1.n3)
    if ($node1.PSObject.Properties.Match('n4').Count -gt 0 -and $node1.n4) { $nIds += $node1.n4 }
    $cx = 0.0; $cy = 0.0
    foreach ($nid in $nIds) { $cx += $nodes[[int]$nid].X; $cy += $nodes[[int]$nid].Y }
    $cx /= $nIds.Count; $cy /= $nIds.Count
    $r = [math]::Sqrt($cx*$cx + $cy*$cy)
    $theta = [math]::Atan2($cy, $cx)
    $srr = $factor * (1.0 - $b*$b / ($r*$r))
    $stt = $factor * (1.0 + $b*$b / ($r*$r))
    $szz = $nu * ($srr + $stt)
    $c = [math]::Cos($theta); $s = [math]::Sin($theta)
    $sxx = $srr * $c*$c + $stt * $s*$s
    $syy = $srr * $s*$s + $stt * $c*$c
    $sxy = ($srr - $stt) * $s*$c
    $rows.Add([pscustomobject]@{
        id = $el.id
        r = [math]::Round($r, 4)
        theta_deg = [math]::Round($theta*180/[math]::PI, 2)
        sxx_fe = [math]::Round([double]$el.sxx, 2)
        sxx_lame = [math]::Round($sxx, 2)
        d_sxx = [math]::Round([math]::Abs([double]$el.sxx - $sxx), 2)
        syy_fe = [math]::Round([double]$el.syy, 2)
        syy_lame = [math]::Round($syy, 2)
        d_syy = [math]::Round([math]::Abs([double]$el.syy - $syy), 2)
        txy_fe = [math]::Round([double]$el.txy, 2)
        txy_lame = [math]::Round($sxy, 2)
        d_txy = [math]::Round([math]::Abs([double]$el.txy - $sxy), 2)
        szz_fe = [math]::Round([double]$el.szz, 2)
        szz_lame = [math]::Round($szz, 2)
        d_szz = [math]::Round([math]::Abs([double]$el.szz - $szz), 2)
    }) | Out-Null
}

$rows | Sort-Object r, theta_deg | Format-Table -AutoSize

$dxx = ($rows | Measure-Object -Property d_sxx -Maximum -Average)
$dyy = ($rows | Measure-Object -Property d_syy -Maximum -Average)
$dtx = ($rows | Measure-Object -Property d_txy -Maximum -Average)
$dzz = ($rows | Measure-Object -Property d_szz -Maximum -Average)
Write-Host ("MAX(|err|) sxx={0:F2} syy={1:F2} txy={2:F2} szz={3:F2}" -f $dxx.Maximum, $dyy.Maximum, $dtx.Maximum, $dzz.Maximum)
Write-Host ("AVG(|err|) sxx={0:F2} syy={1:F2} txy={2:F2} szz={3:F2}" -f $dxx.Average, $dyy.Average, $dtx.Average, $dzz.Average)
