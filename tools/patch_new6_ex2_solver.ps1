<#
.SYNOPSIS
  Patch new_6.dat T3 mesh deck to Ex2 two-layer solver terms (materials, etype, SRF control).

.DESCRIPTION
  Keeps connectivity and nodal coordinates unchanged. Reads horizontal layer-cut metadata from
  Verification/slope/Ex2_twolayerslope/new_6_layer_config.json and assigns etype using the same
  centroid-y band rule as Q8MeshMaterialLayerPlanner.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/patch_new6_ex2_solver.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $InputPath,
    [string] $ConfigPath,
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
if (-not $InputPath) {
    $InputPath = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope\new_6.dat'
}
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope\new_6_layer_config.json'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope\new_6_ex2_solver.dat'
}

function Format-Double([double]$Value) {
    return ([double]$Value).ToString('G10', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Strip-Comment([string]$Line) {
    $bang = $Line.IndexOf('!')
    if ($bang -lt 0) { return $Line.Trim() }
    $before = $Line.Substring(0, $bang).Trim()
    if ($before.Length -gt 0) { return $before }
    return $Line.Substring($bang + 1).Trim()
}

function Read-Config([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Layer config not found: $Path"
    }
    $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($null -eq $json.layerCutY) { throw "Config missing layerCutY" }
    $ids = @($json.layerMaterialIds)
    if ($ids.Count -lt 2) { throw "Config layerMaterialIds must have at least 2 entries (lower, upper)" }
    return [pscustomobject]@{
        CutY = [double]$json.layerCutY
        LayerMaterialIds = $ids
    }
}

function Parse-Mesh([string[]]$Lines) {
    $meshBegin = [Array]::IndexOf($Lines, '! <BEGIN:Mesh>')
    $meshEnd = [Array]::IndexOf($Lines, '! <END:Mesh>')
    if ($meshBegin -lt 0 -or $meshEnd -lt 0) { throw 'Mesh block not found' }

    $coords = @{}
    $conn = New-Object System.Collections.Generic.List[int[]]
    $phase = 'seek'
    $nodeId = 0
    for ($i = $meshBegin + 1; $i -lt $meshEnd; $i++) {
        $raw = $Lines[$i]
        $line = Strip-Comment $raw
        if ($line.Length -eq 0) { continue }
        if ($raw -match '^\s*!\s*etype' -or $line.StartsWith('etype')) { $phase = 'etype'; continue }
        if ($raw -match '^\s*!\s*x\(m\)' -or $line.StartsWith('x(m)')) { $phase = 'coords'; continue }
        if ($raw -match 'connectivity' -or $line.StartsWith('connectivity')) { $phase = 'conn'; continue }

        $tok = $line -split '\s+' | Where-Object { $_.Length -gt 0 }
        switch ($phase) {
            'etype' { continue }
            'coords' {
                if ($tok.Count -ge 2) {
                    $nodeId++
                    $coords[$nodeId] = @([double]$tok[0], [double]$tok[1])
                }
            }
            'conn' {
                if ($tok.Count -ge 3) {
                    $conn.Add(@([int]$tok[0], [int]$tok[1], [int]$tok[2]))
                }
            }
        }
    }

    if ($conn.Count -eq 0) { throw 'No T3 connectivity rows parsed' }
    if ($coords.Count -eq 0) { throw 'No nodal coordinates parsed' }

    return [pscustomobject]@{
        Nels = $conn.Count
        Nn = $coords.Count
        Coords = $coords
        Connectivity = $conn
        MeshBegin = $meshBegin
        MeshEnd = $meshEnd
    }
}

function Get-CentroidY([int[]]$Tri, [hashtable]$Coords) {
    $sum = 0.0
    $n = 0
    foreach ($id in $Tri) {
        if (-not $Coords.ContainsKey($id)) { continue }
        $sum += $Coords[$id][1]
        $n++
    }
    if ($n -eq 0) { return [double]::NaN }
    return $sum / $n
}

function Resolve-Etype([double]$Cy, [double]$YMin, [double]$YMax, [double]$CutY, [int[]]$LayerMaterialIds) {
    $tol = 1e-4
    $breaks = @($YMin, $CutY, $YMax) | Sort-Object -Unique
    if ($breaks.Count -lt 2) { return 1 }

    $layerIdx = 0
    for ($i = 0; $i -lt $breaks.Count - 1; $i++) {
        $lo = [double]$breaks[$i]
        $hi = [double]$breaks[$i + 1]
        $isTop = ($i -eq ($breaks.Count - 2))
        if ($Cy -ge ($lo - $tol) -and (($isTop -and $Cy -le ($hi + $tol)) -or (-not $isTop -and $Cy -lt ($hi - $tol)))) {
            $layerIdx = $i
            break
        }
    }
    if ($Cy -lt $breaks[0]) { $layerIdx = 0 }
    if ($Cy -gt $breaks[-1]) { $layerIdx = $breaks.Count - 2 }

    $matId = $LayerMaterialIds[[Math]::Min($layerIdx, $LayerMaterialIds.Count - 1)]
    $sorted = $LayerMaterialIds | Sort-Object -Unique
    $etype = 1 + [Array]::IndexOf($sorted, $matId)
    if ($etype -lt 1) { $etype = 1 }
    return $etype
}

function Build-EtypeBlock([int[]]$Etypes) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('! etype (1-based material index per element, 20 per line)')
    $col = 0
    foreach ($e in $Etypes) {
        [void]$sb.Append('{0,3}' -f $e)
        $col++
        if ($col % 20 -eq 0) { [void]$sb.AppendLine() }
    }
    if ($col % 20 -ne 0) { [void]$sb.AppendLine() }
    [void]$sb.AppendLine()
    return $sb.ToString()
}

function Build-MaterialsBlock {
    $phi1 = 25.0; $c1 = 1.0; $psi1 = 0.0; $gam = 20.0; $e = 100000.0; $nu = 0.3
    $phi2 = 15.0; $c2 = 0.5; $psi2 = 0.0
    $k01 = 1.0 - [Math]::Sin($phi1 * [Math]::PI / 180.0)
    $k02 = 1.0 - [Math]::Sin($phi2 * [Math]::PI / 180.0)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('! <BEGIN:Materials>')
    [void]$sb.AppendLine('! phi(°)  c(kPa)  psi(°)  gamma(kN/m³)  E(kPa)  v(-)  K0(-)  — one row per property, one value per material')
    [void]$sb.AppendLine("$(Format-Double $phi1)  $(Format-Double $phi2)")
    [void]$sb.AppendLine("$(Format-Double $c1)  $(Format-Double $c2)")
    [void]$sb.AppendLine("$(Format-Double $psi1)  $(Format-Double $psi2)")
    [void]$sb.AppendLine("$(Format-Double $gam)  $(Format-Double $gam)")
    [void]$sb.AppendLine("$(Format-Double $e)  $(Format-Double $e)")
    [void]$sb.AppendLine("$(Format-Double $nu)  $(Format-Double $nu)")
    [void]$sb.AppendLine("$(Format-Double $k01)  $(Format-Double $k02)")
    [void]$sb.AppendLine('! <BEGIN:MaterialModelOptions>')
    [void]$sb.AppendLine('! material_index 1')
    [void]$sb.AppendLine('! failure_criterion MohrCoulomb')
    [void]$sb.AppendLine('! material_type Isotropic')
    [void]$sb.AppendLine('! strength tensile_cutoff=0')
    [void]$sb.AppendLine('! material_index 2')
    [void]$sb.AppendLine('! failure_criterion MohrCoulomb')
    [void]$sb.AppendLine('! material_type Isotropic')
    [void]$sb.AppendLine('! strength tensile_cutoff=0')
    [void]$sb.AppendLine('! <END:MaterialModelOptions>')
    [void]$sb.AppendLine('! <END:Materials>')
    return $sb.ToString()
}

function Build-AnalysisOptionsBlock {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('! <BEGIN:AnalysisOptions>')
    [void]$sb.AppendLine('! failure_criterion MohrCoulomb')
    [void]$sb.AppendLine('! material_type Isotropic')
    [void]$sb.AppendLine('! mesh_type T3')
    [void]$sb.AppendLine('! srf_mode multi')
    [void]$sb.AppendLine('! srf_F 1')
    [void]$sb.AppendLine('! expected_FoS 1.5')
    [void]$sb.AppendLine('! expected_FoS_tolerance_pct 15')
    [void]$sb.AppendLine('! expected_FoS_mode srf')
    [void]$sb.AppendLine('! solver_executable SRF')
    [void]$sb.AppendLine('! <END:AnalysisOptions>')
    return $sb.ToString()
}

$config = Read-Config $ConfigPath
$content = Get-Content -Raw -Path $InputPath
$nl = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
$lines = $content -split "`r?`n"
$mesh = Parse-Mesh $lines

$ys = @($mesh.Coords.Values | ForEach-Object { $_[1] })
$yMin = ($ys | Measure-Object -Minimum).Minimum
$yMax = ($ys | Measure-Object -Maximum).Maximum

$etypes = New-Object System.Collections.Generic.List[int]
$etypeHist = @{}
foreach ($tri in $mesh.Connectivity) {
    $cy = Get-CentroidY $tri $mesh.Coords
    $e = Resolve-Etype $cy $yMin $yMax $config.CutY $config.LayerMaterialIds
    $etypes.Add($e) | Out-Null
    if (-not $etypeHist.ContainsKey($e)) { $etypeHist[$e] = 0 }
    $etypeHist[$e]++
}

Write-Host "Parsed mesh: nels=$($mesh.Nels) nn=$($mesh.Nn) y=[$yMin,$yMax] cutY=$($config.CutY)"
Write-Host ("etype mix: " + (($etypeHist.GetEnumerator() | Sort-Object Name | ForEach-Object { "etype$($_.Name)=$($_.Value)" }) -join ', '))

# Preserve prefix through groundwater, rebuild header/materials/mesh prefix
$headParts = New-Object System.Collections.Generic.List[string]
$idx = 0
while ($idx -lt $lines.Count) {
    $headParts.Add($lines[$idx]) | Out-Null
    if ($lines[$idx] -eq '! <BEGIN:Header>') { break }
    $idx++
}
if ($idx -ge $lines.Count) { throw 'Header begin not found' }

$out = New-Object System.Text.StringBuilder
[void]$out.Append(($headParts -join $nl))
[void]$out.Append($nl)
[void]$out.Append((Build-AnalysisOptionsBlock))
[void]$out.Append($nl)
[void]$out.Append($nl)
[void]$out.Append($nl)
[void]$out.Append('! nels  nn  np_types')
[void]$out.Append($nl)
[void]$out.Append("$($mesh.Nels)  $($mesh.Nn)  2")
[void]$out.Append($nl)
[void]$out.Append('! <END:Header>')
[void]$out.Append($nl)
[void]$out.Append($nl)
[void]$out.Append((Build-MaterialsBlock))
[void]$out.Append($nl)
[void]$out.Append('! <BEGIN:Mesh>')
[void]$out.Append($nl)
[void]$out.Append((Build-EtypeBlock $etypes.ToArray()))

# Copy original mesh body (coords + connectivity) skipping old etype if present
$phase = 'seek'
for ($i = $mesh.MeshBegin + 1; $i -lt $mesh.MeshEnd; $i++) {
    $raw = $lines[$i]
    $line = Strip-Comment $raw
    if ($line.Length -eq 0) { continue }
    if ($raw -match '^\s*!\s*etype' -or $line.StartsWith('etype')) { $phase = 'etype'; continue }
    if ($raw -match '^\s*!\s*x\(m\)' -or $line.StartsWith('x(m)')) { $phase = 'coords'; [void]$out.Append($raw); [void]$out.Append($nl); continue }
    if ($raw -match 'connectivity' -or $line.StartsWith('connectivity')) { $phase = 'conn'; [void]$out.Append($raw); [void]$out.Append($nl); continue }
    if ($phase -eq 'etype') { continue }
    [void]$out.Append($raw)
    [void]$out.Append($nl)
}
[void]$out.Append('! <END:Mesh>')
[void]$out.Append($nl)

# Append tail from Bcs onward, replacing SolverControl
$tailStart = [Array]::IndexOf($lines, '! <BEGIN:Bcs>')
if ($tailStart -lt 0) { throw 'Bcs block not found' }
$inSolver = $false
for ($i = $tailStart; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -eq '! <BEGIN:SolverControl>') {
        [void]$out.Append('! <BEGIN:SolverControl>')
        [void]$out.Append($nl)
        [void]$out.Append('! tol  limit  incs  layers')
        [void]$out.Append($nl)
        [void]$out.Append('0.0001  500  20  1')
        [void]$out.Append($nl)
        [void]$out.Append('! nsrf 6')
        [void]$out.Append($nl)
        [void]$out.Append('! srf_F_trials 1  1.1  1.2  1.3  1.4  1.5')
        [void]$out.Append($nl)
        [void]$out.Append('! <END:SolverControl>')
        [void]$out.Append($nl)
        $inSolver = $true
        continue
    }
    if ($inSolver) {
        if ($lines[$i] -eq '! <END:SolverControl>') { $inSolver = $false }
        continue
    }
    [void]$out.Append($lines[$i])
    [void]$out.Append($nl)
}

$result = $out.ToString()
Set-Content -Path $OutputPath -Value $result -NoNewline
Write-Host "Wrote $($OutputPath)"
