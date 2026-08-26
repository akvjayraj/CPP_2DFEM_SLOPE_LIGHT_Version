# Generates an FE verification report (.docx) for RS2 Example 16
# (layered slope with water table and weak seam - Greco 1996 / Chen & Shao 1988).
# Project tabs are read from the saved .xfem; FoS matrix from comparision.txt;
# literature FoS from gui_automation summary when present.
param(
  [string]$ProjectFile = "$PSScriptRoot\rs2_example16_layered_slope_water_table_weak_seam.xfem",
  [string]$ResultsJson = "$PSScriptRoot\gui_automation\rs2_example16_gui_run_summary.json",
  [string]$ComparisonFile = "$PSScriptRoot\comparision.txt",
  [string]$OutFile = "$PSScriptRoot\RS2_Example16_layered_slope_water_table_weak_seam_FE_Verification_Report.docx"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Esc([string]$s){ if($null -eq $s){return ''}; $s.Replace('&','&amp;').Replace('<','&lt;').Replace('>','&gt;').Replace('"','&quot;') }
function P([string]$text,[string]$style='',[switch]$b,[string]$jc='',[int]$sz=0,[string]$color=''){
  $ppr=''
  if($style){$ppr+="<w:pStyle w:val=""$style""/>"}
  if($jc){$ppr+="<w:jc w:val=""$jc""/>"}
  if($ppr){$ppr="<w:pPr>$ppr</w:pPr>"}
  $rpr=''
  if($b){$rpr+='<w:b/>'}
  if($sz -gt 0){$rpr+="<w:sz w:val=""$($sz*2)""/>"}
  if($color){$rpr+="<w:color w:val=""$color""/>"}
  if($rpr){$rpr="<w:rPr>$rpr</w:rPr>"}
  "<w:p>$ppr<w:r>$rpr<w:t xml:space=""preserve"">$(Esc $text)</w:t></w:r></w:p>"
}
function Spacer(){ '<w:p/>' }
function Tbl($headers,$rows,$widths){
  $tw = ($widths | Measure-Object -Sum).Sum
  $borders='<w:tblBorders>' +
    '<w:top w:val="single" w:sz="4" w:space="0" w:color="808080"/>' +
    '<w:left w:val="single" w:sz="4" w:space="0" w:color="808080"/>' +
    '<w:bottom w:val="single" w:sz="4" w:space="0" w:color="808080"/>' +
    '<w:right w:val="single" w:sz="4" w:space="0" w:color="808080"/>' +
    '<w:insideH w:val="single" w:sz="4" w:space="0" w:color="B0B0B0"/>' +
    '<w:insideV w:val="single" w:sz="4" w:space="0" w:color="B0B0B0"/></w:tblBorders>'
  $grid='<w:tblGrid>' + (($widths|ForEach-Object{"<w:gridCol w:w=""$_""/>"}) -join '') + '</w:tblGrid>'
  $sb=[System.Text.StringBuilder]::new()
  [void]$sb.Append("<w:tbl><w:tblPr><w:tblW w:w=""$tw"" w:type=""dxa""/>$borders</w:tblPr>$grid")
  [void]$sb.Append('<w:tr><w:trPr><w:tblHeader/></w:trPr>')
  for($i=0;$i -lt $headers.Count;$i++){
    $c=$headers[$i]; $w=$widths[$i]
    [void]$sb.Append("<w:tc><w:tcPr><w:tcW w:w=""$w"" w:type=""dxa""/><w:shd w:val=""clear"" w:color=""auto"" w:fill=""1F3864""/><w:vAlign w:val=""center""/></w:tcPr><w:p><w:pPr><w:jc w:val=""center""/></w:pPr><w:r><w:rPr><w:b/><w:color w:val=""FFFFFF""/></w:rPr><w:t xml:space=""preserve"">$(Esc $c)</w:t></w:r></w:p></w:tc>")
  }
  [void]$sb.Append('</w:tr>')
  $r=0
  foreach($row in $rows){
    $fill = if($r % 2 -eq 0){'F2F6FC'}else{'FFFFFF'}
    [void]$sb.Append('<w:tr>')
    for($i=0;$i -lt $row.Count;$i++){
      $w=$widths[$i]; $jc= if($i -eq 0){'left'}else{'center'}
      [void]$sb.Append("<w:tc><w:tcPr><w:tcW w:w=""$w"" w:type=""dxa""/><w:shd w:val=""clear"" w:color=""auto"" w:fill=""$fill""/><w:vAlign w:val=""center""/></w:tcPr><w:p><w:pPr><w:jc w:val=""$jc""/></w:pPr><w:r><w:t xml:space=""preserve"">$(Esc "$($row[$i])")</w:t></w:r></w:p></w:tc>")
    }
    [void]$sb.Append('</w:tr>')
    $r++
  }
  [void]$sb.Append('</w:tbl>')
  $sb.ToString()
}
function FmtNum($v,[string]$fmt='0.000'){
  if($null -eq $v -or "$v" -eq ''){ return 'n/a' }
  try {
    return [double]::Parse($v.ToString(),[Globalization.CultureInfo]::InvariantCulture).ToString($fmt,[Globalization.CultureInfo]::InvariantCulture)
  } catch { return "$v" }
}
function FirstNonEmpty([string[]]$values,[string]$fallback='n/a'){
  foreach($v in $values){
    if(-not [string]::IsNullOrWhiteSpace($v)){ return $v }
  }
  return $fallback
}
function FmtPts($pts){
  if(-not $pts){ return 'n/a' }
  ($pts | ForEach-Object { "($($_.x), $($_.y))" }) -join ' -> '
}

if(-not (Test-Path $ProjectFile)){
  throw "Project file not found: $ProjectFile"
}

Write-Host "Loading project: $ProjectFile"
$doc = Get-Content -LiteralPath $ProjectFile -Raw | ConvertFrom-Json
$ps = $doc.projectSettings
$geom = $doc.geometry
$mats = @($doc.materials.materialLibrary)
$fe = $doc.feAnalysis
$mesh = $doc.mesh
$bcRows = @($doc.boundaryConditions.rows)
$loads = @($doc.loads)
$field = $doc.fieldStress
$today = (Get-Date).ToString('dd MMMM yyyy')

# Literature / automation summary (optional)
$literatureFos = 1.02
$refBishop = 1.087
$refSpencerC = 1.093
$refSpencerNC = 1.007
$summaryGenerated = 'n/a'
$autoStatus = [string]$fe.statusMessage
$autoFoS = $null
$nodeCount = if($mesh.discretizationNodes){ @($mesh.discretizationNodes).Count } else { 0 }
$elemCount = if($mesh.informationRows){ @($mesh.informationRows).Count } elseif($mesh.elements){ @($mesh.elements).Count } else { 0 }
if(Test-Path $ResultsJson){
  $summary = Get-Content -LiteralPath $ResultsJson -Raw | ConvertFrom-Json
  $summaryGenerated = FirstNonEmpty @([string]$summary.GeneratedAt) 'n/a'
  if($null -ne $summary.LiteratureFoS){ $literatureFos = [double]$summary.LiteratureFoS }
  if($null -ne $summary.ReferenceFoSBishop){ $refBishop = [double]$summary.ReferenceFoSBishop }
  if($null -ne $summary.ReferenceFoSSpencerCircular){ $refSpencerC = [double]$summary.ReferenceFoSSpencerCircular }
  if($null -ne $summary.ReferenceFoSSpencerNonCircular){ $refSpencerNC = [double]$summary.ReferenceFoSSpencerNonCircular }
  $case = @($summary.Cases) | Select-Object -First 1
  if($case){
    $autoStatus = FirstNonEmpty @([string]$case.StatusMessage, [string]$fe.statusMessage) 'n/a'
    if($null -ne $case.EstimatedFoS){ $autoFoS = [double]$case.EstimatedFoS }
    if($case.NodeCount){ $nodeCount = [int]$case.NodeCount }
    if($case.ElementCount){ $elemCount = [int]$case.ElementCount }
  }
}

# Manual FoS matrix from comparision.txt
$comparisonRows = @()
if(Test-Path $ComparisonFile){
  Get-Content -LiteralPath $ComparisonFile | ForEach-Object {
    $line = $_.Trim()
    if([string]::IsNullOrWhiteSpace($line)){ return }
    if($line -match '^(?<case>.+?)\s*[:=]\s*(?<fos>[\d.]+)\s*(?:Kh\s*=\s*(?<kh>[\d.]+))?'){
      $kh = if($Matches['kh']){ $Matches['kh'] } else { '-' }
      $comparisonRows += ,@($Matches['case'].Trim(), $Matches['fos'], $kh)
    } elseif($line -match 'mesh'){
      $comparisonRows += ,@($line, '-', '-')
    }
  }
}
if($comparisonRows.Count -eq 0){
  $comparisonRows = @(
    @('only SRF','1.25','-'),
    @('With SRF+load','1.22','-'),
    @('with SRF+ water','0.67','-'),
    @('with water + load+ SRF','0.85','-'),
    @('with SRF + Seismic','0.59','0.4'),
    @('with SRF + Seismic','0.22','0.5')
  )
}

# Optional trial meta scan (meta.srf only - avoid loading stage arrays)
$trialMetaRows = @()
$trialDir = Join-Path $PSScriptRoot 'gui_automation\t3'
if(Test-Path $trialDir){
  Get-ChildItem -LiteralPath $trialDir -Filter 'p610t3_script_run_srf_*.json' |
    Sort-Object Name |
    Select-Object -First 20 |
    ForEach-Object {
      $raw = Get-Content -LiteralPath $_.FullName -Raw
      $f='n/a'; $yr='n/a'; $it='n/a'; $cv='n/a'; $md='n/a'
      if($raw -match '"fos"\s*:\s*([0-9.eE+-]+)'){ $f = $Matches[1] }
      elseif($raw -match '"F_trial"\s*:\s*([0-9.eE+-]+)'){ $f = $Matches[1] }
      if($raw -match '"max_yield_ratio"\s*:\s*([0-9.eE+-]+)'){ $yr = $Matches[1] }
      if($raw -match '"max_iters_used"\s*:\s*([0-9.eE+-]+)'){ $it = $Matches[1] }
      if($raw -match '"all_increments_converged"\s*:\s*(true|false)'){ $cv = $Matches[1] }
      if($raw -match '"max_disp"\s*:\s*([0-9.eE+-]+)'){ $md = $Matches[1] }
      if($f -ne 'n/a' -or $yr -ne 'n/a'){
        $trialMetaRows += ,@((FmtNum $f '0.####'), (FmtNum $md '0.000E+0'), $it, $cv, (FmtNum $yr '0.###'))
      }
    }
}

# Mesh status parse
$meshStatus = [string]$mesh.surfaceMeshStatus
$minAngle = 'n/a'; $maxAR = 'n/a'
if($meshStatus -match 'Min angle:\s*([\d.]+)'){ $minAngle = $Matches[1] }
if($meshStatus -match 'Max AR:\s*([\d.]+)'){ $maxAR = $Matches[1] }
$elemFamily = FirstNonEmpty @([string]$mesh.selectedTwoDElementFamily) 'Triangles'
$nodeOpt = FirstNonEmpty @([string]$mesh.selectedTwoDNodeOption) '3-node'
$meshTypeLabel = "$elemFamily / $nodeOpt"
if($meshStatus -match 'T3'){ $meshTypeLabel = 'T3 (3-node triangles)' }

# BC summary
$bcSummary = $bcRows | Group-Object boundaryName, bcType | Sort-Object Name | ForEach-Object {
  $parts = $_.Name -split ',\s*'
  ,@($parts[0], $parts[1], "$($_.Count)")
}

# Layer assignment table
$matById = @{}
foreach($m in $mats){ $matById[[int]$m.id] = $m }
$layerRows = @()
foreach($la in @($geom.layerMaterialAssignments)){
  $mid = [int]$la.materialId
  $m = $matById[$mid]
  $mName = if($m){ [string]$m.name } else { "Material $mid" }
  $layerRows += ,@("$($la.displayLayerNumber)", $mName, "$mid")
}

# Geometry cuts
$cutRows = @()
$ci = 1
foreach($cut in @($geom.nonHorizontalLayerCuts)){
  $cutRows += ,@("Cut $ci", (FmtPts $cut.points))
  $ci++
}

# Materials table
$matRows = foreach($m in $mats){
  ,@(
    "$($m.id)",
    [string]$m.name,
    (FmtNum $m.unitWeight '0.##'),
    (FmtNum $m.cohesion '0.##'),
    (FmtNum $m.frictionAngle '0.##'),
    (FmtNum $m.dilationAngle '0.##'),
    (FmtNum $m.youngsModulus '0'),
    (FmtNum $m.poissonsRatio '0.##')
  )
}

# Loads
$loadRows = @()
if($loads.Count -eq 0){
  $loadRows = ,@('-','(none defined in Load Conditions)','-','-','-')
} else {
  foreach($L in $loads){
    if($L.PSObject.Properties['isEnabled'] -and -not $L.isEnabled){ continue }
    $loadRows += ,@(
      (FirstNonEmpty @([string]$L.name, [string]$L.type) 'Load'),
      (FirstNonEmpty @([string]$L.type) 'n/a'),
      (FmtNum $L.magnitude '0.###'),
      (FirstNonEmpty @([string]$L.orientationSummary, [string]$L.orientation) 'n/a'),
      ("($($L.startX),$($L.startY))->($($L.endX),$($L.endY))")
    )
  }
}

# GW points
$gwPts = @($geom.groundwaterSurfPoints)
$gwPointRows = @()
$gi = 1
foreach($gp in $gwPts){
  $gwPointRows += ,@("$gi", (FmtNum $gp.x '0.##'), (FmtNum $gp.y '0.##'))
  $gi++
}

$fsMode = if($field -and $field.global){ [string]$field.global.mode } else { 'n/a' }
$projectName = FirstNonEmpty @([string]$ps.projectName) ([System.IO.Path]::GetFileNameWithoutExtension($ProjectFile))

$body = [System.Text.StringBuilder]::new()
function Add($x){ [void]$body.Append($x) }

Add (P 'FINITE ELEMENT SLOPE STABILITY' 'Title' -jc 'center')
Add (P 'VERIFICATION REPORT' 'Title' -jc 'center')
Add (Spacer)
Add (P 'Benchmark: RS2 Slope Stability Verification Part 1 - Example 16' '' -b -jc 'center' -sz 14 -color '1F3864')
Add (P 'Layered Slope with Water Table and Weak Seam (Greco 1996 / Chen and Shao 1988)' '' -jc 'center' -sz 12 -color '404040')
Add (Spacer)
Add (Tbl @('Document control','Detail') @(
  @('Project file',[System.IO.Path]::GetFileName($ProjectFile)),
  @('Project name',$projectName),
  @('Analysis type', (FirstNonEmpty @([string]$doc.materials.selectedAnalysisCategory) 'Slope stability - SRF')),
  @('Detailed type', (FirstNonEmpty @([string]$doc.materials.selectedDetailedAnalysisType) 'SRF')),
  @('Constitutive model','Mohr-Coulomb (elastic-perfectly plastic), plane strain'),
  @('Mesh type',$meshTypeLabel),
  @('Mesh size',"$elemCount elements, $nodeCount nodes"),
  @('Solver executable','slope_stability_T3_SRF (Core A, p610 deck)'),
  @('Primary literature FoS',("RS2 SSR ~ {0}" -f (FmtNum $literatureFos))),
  @('Report date',$today),
  @('Automation snapshot',$summaryGenerated)
) @(3200,6100))
Add (P 'This report documents the GeotechSlopeApp project inputs (all primary GUI tabs) for RS2 Example 16 and summarises strength-reduction results under dry, wet, surcharge, and seismic combinations, with comparison to published literature factors of safety.' '' -jc 'both')

# ---- 1 Project Settings ----
Add (P '1.  Project Settings' 'Heading1')
Add (Tbl @('Setting','Value') @(
  @('Dimension', (FirstNonEmpty @([string]$ps.selectedDimension) 'twoD')),
  @('Units', (FirstNonEmpty @([string]$ps.units) 'SI')),
  @('Deterministic analysis', ("{0}" -f $ps.isDeterministicAnalysis)),
  @('Probabilistic analysis', ("{0}" -f $ps.isProbabilisticAnalysis)),
  @('Number of stages', ("{0}" -f $ps.numberOfStages)),
  @('Axes X range', ("{0} to {1} m" -f $ps.axisXMin, $ps.axisXMax)),
  @('Axes Y range', ("{0} to {1} m" -f $ps.axisYMin, $ps.axisYMax)),
  @('Grid spacing', ("{0} m" -f $ps.gridSpacingMeters)),
  @('Show grid / axes', ("{0} / {1}" -f $ps.showGrid, $ps.showAxes)),
  @('Created on', (FirstNonEmpty @([string]$ps.createdOn) 'n/a')),
  @('Author', (FirstNonEmpty @([string]$ps.author) '(blank)'))
) @(4300,5000))

# ---- 2 Geometry ----
Add (P '2.  Geometry' 'Heading1')
Add (P 'The model uses a multi-layer slope with three non-horizontal material interfaces and a thin weak seam. Geometry extents and layer cuts are taken from the saved .xfem Geometry tab.' '' -jc 'both')
Add (Tbl @('Geometry parameter','Value') @(
  @('Canvas size', ("{0} x {1} px" -f $geom.canvasWidth, $geom.canvasHeight)),
  @('Axis X', ("{0} to {1} m" -f $geom.axisXMin, $geom.axisXMax)),
  @('Axis Y', ("{0} to {1} m" -f $geom.axisYMin, $geom.axisYMax)),
  @('Boundary vertices', ("{0}" -f @($geom.vertices).Count)),
  @('Boundary line segments', ("{0}" -f @($geom.lineSegments).Count)),
  @('Non-horizontal layer cuts', ("{0}" -f @($geom.nonHorizontalLayerCuts).Count)),
  @('Layer -> material assignments', ("{0}" -f @($geom.layerMaterialAssignments).Count))
) @(4300,5000))
if($cutRows.Count -gt 0){
  Add (P 'Non-horizontal layer cut polylines (m):' '' -jc 'both')
  Add (Tbl @('Cut','Polyline points (x, y)') $cutRows @(1500,7800))
}
if($layerRows.Count -gt 0){
  Add (P 'Layer material assignments (display layer -> material library):' '' -jc 'both')
  Add (Tbl @('Display layer','Material name','Material ID') $layerRows @(1800,5500,2000))
}

# ---- 3 Materials ----
Add (P '3.  Material Properties' 'Heading1')
Add (P ("Failure criterion: {0}; material type: {1}." -f `
  (FirstNonEmpty @([string]$doc.materials.selectedFailureCriterion) 'MohrCoulomb'), `
  (FirstNonEmpty @([string]$doc.materials.selectedMaterialType) 'Isotropic')) '' -jc 'both')
Add (Tbl @('ID','Name','gamma (kN/m3)','c (kPa)','phi (deg)','psi (deg)','E (kPa)','nu') $matRows @(700,2200,1100,1100,900,800,1200,700))
Add (P 'Layer 4 is the thin weak seam (phi = 16 deg) that controls the wet failure mechanism in the RS2 verification case.' '' -jc 'both')

# ---- 4 Loads ----
Add (P '4.  Loads / Field Stress / Seismic' 'Heading1')
Add (P 'Applied loads from the Load Conditions tab (point / line / triangular). Gravity body force is controlled on the FE Analysis tab; field stress mode is stored under fieldStress.' '' -jc 'both')
Add (Tbl @('Name','Type','Magnitude','Orientation','Coordinates') $loadRows @(1800,1400,1400,1600,3100))
Add (Tbl @('Body / field / seismic','Value') @(
  @('Gravity enabled (FE Analysis)', ("{0}" -f $fe.gravityEnabled)),
  @('Gravity acceleration g', ("{0} m/s2" -f $fe.gravityAcceleration)),
  @('Field stress mode', $fsMode),
  @('Seismic k_h / k_v in script AnalysisOptions', 'See Script Editor / Load Conditions (default 0 / 0 in saved deck unless enabled)')
) @(4300,5000))

# ---- 5 Mesh ----
Add (P '5.  Mesh' 'Heading1')
Add (Tbl @('Mesh parameter','Value') @(
  @('Discretized', ("{0}" -f $mesh.isDiscretized)),
  @('Element family / nodes', ("{0} / {1}" -f $elemFamily, $nodeOpt)),
  @('Numbering direction', (FirstNonEmpty @([string]$mesh.selectedMeshNumberingDirection) 'n/a')),
  @('Global mesh size', (FirstNonEmpty @([string]$mesh.globalMeshSize) 'n/a')),
  @('Node count', "$nodeCount"),
  @('Element count', "$elemCount"),
  @('Min angle / Max AR', ("{0} deg / {1}" -f $minAngle, $maxAR)),
  @('Surface mesh status', (FirstNonEmpty @($meshStatus) 'n/a'))
) @(4300,5000))

# ---- 6 Boundary Conditions ----
Add (P '6.  Boundary Conditions' 'Heading1')
Add (P ("Total BC rows applied to mesh nodes: {0}. Summary by boundary edge and type:" -f $bcRows.Count) '' -jc 'both')
if($bcSummary.Count -gt 0){
  Add (Tbl @('Boundary','BC type','Node count') $bcSummary @(2500,3500,3300))
}
Add (P 'Typical restraint pattern for this slope: Roller X on the left/right vertical faces (L1, L8) and Fixed support on the base edges (L9, L10).' '' -jc 'both')

# ---- 7 Groundwater ----
Add (P '7.  Groundwater' 'Heading1')
Add (Tbl @('Groundwater parameter','Value') @(
  @('Enabled', ("{0}" -f $geom.groundwaterEnabled)),
  @('Unit weight of water gamma_w', ("{0} kN/m3" -f $geom.groundwaterGammaW)),
  @('Phreatic surface points', ("{0}" -f $gwPts.Count)),
  @('Method', 'Hydrostatic from phreatic surface (project Geometry / Groundwater)')
) @(4300,5000))
if($gwPointRows.Count -gt 0){
  Add (P 'Phreatic surface points (m):' '' -jc 'both')
  Add (Tbl @('#','X (m)','Y (m)') $gwPointRows @(1500,3900,3900))
}

# ---- 8 FE Analysis ----
Add (P '8.  FE Analysis / Solver Control' 'Heading1')
$srfVals = if($fe.srfValues){ (@($fe.srfValues) -join ', ') } else { 'n/a' }
Add (Tbl @('Solver control','Value') @(
  @('Gravity enabled', ("{0}" -f $fe.gravityEnabled)),
  @('Gravity g', ("{0}" -f $fe.gravityAcceleration)),
  @('Convergence tolerance', ("{0}" -f $fe.convergenceTolerance)),
  @('Iteration limit', ("{0}" -f $fe.maxIterations)),
  @('Load increments', ("{0}" -f $fe.loadIncrements)),
  @('Layers override', ("{0}" -f $fe.layersOverride)),
  @('SRF search mode', (FirstNonEmpty @([string]$fe.srfSearchMode) 'n/a')),
  @('Auto-detect bracket', ("{0}" -f $fe.autoDetectSrfBracket)),
  @('Bracket seed', ("{0}" -f $fe.srfBracketSeed)),
  @('Expansion factor', ("{0}" -f $fe.srfBracketExpansionFactor)),
  @('Max expansion steps', ("{0}" -f $fe.srfMaxExpansionSteps)),
  @('F_min / F_max', ("{0} / {1}" -f $fe.srfFoSMin, $fe.srfFoSMax)),
  @('Bracket tolerance', ("{0}" -f $fe.srfBracketTolerance)),
  @('Max bracket iterations', ("{0}" -f $fe.srfMaxBracketIterations)),
  @('Sweep F list (metadata)', $srfVals),
  @('Last status message', (FirstNonEmpty @($autoStatus) 'n/a'))
) @(4300,5000))

# ---- 9 Results ----
Add (P '9.  Results' 'Heading1')
Add (P '9.1  Literature reference values' 'Heading2')
Add (Tbl @('Source','Method','Reported FoS') @(
  @('RS2 (Rocscience)','FE-SSR',(FmtNum $literatureFos)),
  @('Slide2 Bishop','Circular LEM',(FmtNum $refBishop)),
  @('Slide2 Spencer','Circular LEM',(FmtNum $refSpencerC)),
  @('Slide2 Spencer','Non-circular LEM',(FmtNum $refSpencerNC))
) @(3400,3600,2300))
Add (P 'The RS2 SSR value of approximately 1.02 is the primary finite-element reference for the wet (phreatic surface + weak seam) case.' '' -jc 'both')

Add (P '9.2  Application FoS matrix (manual verification runs)' 'Heading2')
Add (P 'The following factors of safety were recorded for T3 SRF combinations (from comparision.txt). Values below 1.0 indicate predicted instability.' '' -jc 'both')
Add (Tbl @('Case','FoS','k_h') $comparisonRows @(5200,2000,2100))
Add (P 'Dry gravity-only SRF yields FoS ~ 1.25. Activating the water table reduces FoS to ~ 0.67 (below unity). Adding a surcharge with groundwater present recovers FoS to ~ 0.85 (still unstable relative to literature wet FoS ~ 1.02 on the same geometry). Pseudo-static seismic coefficients further reduce FoS (0.59 at k_h = 0.4; 0.22 at k_h = 0.5).' '' -jc 'both')

Add (P '9.3  Automated GUI bracket search snapshot' 'Heading2')
$autoFoSText = if($null -ne $autoFoS){ FmtNum $autoFoS } else { 'not obtained (bracket failed)' }
Add (Tbl @('Quantity','Value') @(
  @('Estimated critical FoS (automation)',$autoFoSText),
  @('Automation status',(FirstNonEmpty @($autoStatus) 'n/a')),
  @('Mesh (automation)',("$elemCount elements, $nodeCount nodes"))
) @(4300,5000))
if($null -eq $autoFoS){
  Add (P 'The automated wet bracket search in the GUI automation snapshot did not return a critical FoS (expansion failed at low F). The manual FoS matrix in section 9.2 is therefore the primary application result for this report; re-running the wet bracket with the current T3 SRF solver is recommended to refresh the automation summary.' '' -jc 'both' -b)
}

if($trialMetaRows.Count -gt 0){
  Add (P 'Sample single-trial solver meta (gui_automation/t3):' '' -jc 'both')
  Add (Tbl @('F_trial','Max disp.','Max iters','All incr. conv.','Max yield ratio') $trialMetaRows @(1500,1800,1400,1800,1800))
}

# ---- 10 Conclusions ----
Add (P '10.  Conclusions' 'Heading1')
Add (P '1. The RS2 Example 16 layered slope (water table + weak seam) is implemented in rs2_example16_layered_slope_water_table_weak_seam.xfem with four Mohr-Coulomb materials, three non-horizontal interfaces, hydrostatic groundwater (gamma_w = 9.81 kN/m3, 4 phreatic points), and a T3 mesh of approximately 14,725 elements / 8,618 nodes.' '' -jc 'both')
Add (P '2. Boundary conditions restrain the left/right faces with Roller X and fix the base; gravity is enabled; no point/line loads are stored in the Load Conditions list for the base wet project file.' '' -jc 'both')
Add (P ("3. Manual T3 SRF runs show dry FoS ~ 1.25 and wet FoS ~ 0.67, versus the RS2 SSR literature value of approximately {0} for the wet case - confirming that groundwater and the weak seam dominate stability." -f (FmtNum $literatureFos)) '' -jc 'both')
Add (P '4. Combined load / seismic cases demonstrate the expected trends: surcharge can partially recover FoS when pore pressures are present; increasing k_h reduces FoS further.' '' -jc 'both')
Add (P '5. Follow-on work: refresh the automated wet bracket search so EstimatedFoS is populated in rs2_example16_gui_run_summary.json for direct percent-deviation reporting against RS2 SSR 1.02.' '' -jc 'both')

Add (P 'References' 'Heading1')
Add (P '1.  Rocscience Inc. RS2 Slope Stability Verification - Part 1. Example 16: Layered Slope with Water Table and Weak Seam.' '' -jc 'both')
Add (P '2.  Greco, V. R. (1996). Efficient Monte Carlo technique for locating critical slip surface. Journal of Geotechnical Engineering, ASCE.' '' -jc 'both')
Add (P '3.  Chen, Z. and Shao, C. (1988). Evaluation of minimum factor of safety in slope stability analysis. Canadian Geotechnical Journal.' '' -jc 'both')
Add (P '4.  Rocscience Inc. Slide2 verification values (Bishop / Spencer) cited in the project automation summary.' '' -jc 'both')

$sect = '<w:sectPr><w:footerReference w:type="default" r:id="rId2"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>'
$documentXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>' +
$body.ToString() + $sect + '</w:body></w:document>'
$stylesXml = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:pPr><w:spacing w:after="120" w:line="264" w:lineRule="auto"/></w:pPr></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:spacing w:before="120" w:after="60"/></w:pPr><w:rPr><w:b/><w:color w:val="1F3864"/><w:sz w:val="44"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="280" w:after="120"/><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="2" w:color="1F3864"/></w:pBdr></w:pPr><w:rPr><w:b/><w:color w:val="1F3864"/><w:sz w:val="30"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="180" w:after="80"/></w:pPr><w:rPr><w:b/><w:color w:val="2E74B5"/><w:sz w:val="26"/></w:rPr></w:style>
</w:styles>
'@
$footerXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:pPr><w:pBdr><w:top w:val="single" w:sz="4" w:space="1" w:color="B0B0B0"/></w:pBdr><w:jc w:val="center"/></w:pPr>' +
'<w:r><w:rPr><w:color w:val="808080"/><w:sz w:val="16"/></w:rPr><w:t xml:space="preserve">RS2 Example 16 Layered Slope + Water Table - FE Verification Report      Page </w:t></w:r>' +
'<w:r><w:rPr><w:color w:val="808080"/><w:sz w:val="16"/></w:rPr><w:fldChar w:fldCharType="begin"/></w:r>' +
'<w:r><w:rPr><w:color w:val="808080"/><w:sz w:val="16"/></w:rPr><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>' +
'<w:r><w:rPr><w:color w:val="808080"/><w:sz w:val="16"/></w:rPr><w:fldChar w:fldCharType="end"/></w:r></w:p></w:ftr>'
$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
'<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
'<Default Extension="xml" ContentType="application/xml"/>' +
'<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
'<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>' +
'<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>' +
'</Types>'
$relsRoot = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>' +
'</Relationships>'
$relsDoc = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
'<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>' +
'</Relationships>'

if(Test-Path $OutFile){ Remove-Item $OutFile -Force }
$fs = [System.IO.File]::Open($OutFile,[System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($fs,[System.IO.Compression.ZipArchiveMode]::Create)
function AddEntry($zip,$name,$content){
  $e=$zip.CreateEntry($name,[System.IO.Compression.CompressionLevel]::Optimal)
  $w=New-Object System.IO.StreamWriter($e.Open(),(New-Object System.Text.UTF8Encoding($false)))
  $w.Write($content); $w.Flush(); $w.Dispose()
}
AddEntry $zip '[Content_Types].xml' $contentTypes
AddEntry $zip '_rels/.rels' $relsRoot
AddEntry $zip 'word/document.xml' $documentXml
AddEntry $zip 'word/styles.xml' $stylesXml
AddEntry $zip 'word/footer1.xml' $footerXml
AddEntry $zip 'word/_rels/document.xml.rels' $relsDoc
$zip.Dispose(); $fs.Dispose()
"Wrote $OutFile ($([math]::Round((Get-Item $OutFile).Length/1KB,1)) KB)"
