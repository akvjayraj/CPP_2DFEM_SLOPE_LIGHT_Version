param([string]$Path = "sample.json")
$raw = Get-Content -Raw -Path $Path
$d   = $raw | ConvertFrom-Json

# Global bbox
$xs = $d.nodes | ForEach-Object { $_.x }
$ys = $d.nodes | ForEach-Object { $_.y }
$xmin = ($xs | Measure-Object -Min).Minimum
$xmax = ($xs | Measure-Object -Max).Maximum
$ymin = ($ys | Measure-Object -Min).Minimum
$ymax = ($ys | Measure-Object -Max).Maximum
"mesh bbox x:[{0:F2},{1:F2}]  y:[{2:F2},{3:F2}]" -f $xmin, $xmax, $ymin, $ymax

# Element centroids + σyy per stage
$nodeById = @{}
foreach ($n in $d.nodes) { $nodeById[$n.id] = $n }

# Detect cavern elements = those excavated at stage 2
$caveIds = @{}
if ($d.stages.Count -ge 3) {
  foreach ($e in $d.elements) {
    if ($e.active -eq 0) { $caveIds[$e.id] = $true }
  }
  # Exclude tunnel (stage-1 removals) — they appear dark in stage 1 too.
  # Instead, use a y-band: cavern is upper half of excavated elements.
  $excYs = @()
  foreach ($e in $d.elements) {
    if ($e.active -ne 0) { continue }
    $a = $nodeById[$e.n1]; $b = $nodeById[$e.n2]; $c = $nodeById[$e.n3]
    $yc = ($a.y + $b.y + $c.y) / 3
    $excYs += ,@($e.id, $yc)
  }
  if ($excYs.Count -gt 0) {
    $sortedY = $excYs | ForEach-Object { $_[1] } | Sort-Object
    $median = $sortedY[[int]($sortedY.Count/2)]
    # "cavern" = upper (larger y) excavated elements;
    # "tunnel" = lower (smaller y).
    $caveIds = @{}; $tunIds = @{}
    foreach ($p in $excYs) {
      if ($p[1] -ge $median) { $caveIds[$p[0]] = $true }
      else                   { $tunIds[$p[0]]  = $true }
    }
  }
}

$stage1 = $d.stages | Where-Object { $_.index -eq 1 } | Select-Object -First 1
if ($null -eq $stage1 -or $null -eq $stage1.element_stresses) {
  "no stage 1 element_stresses"; exit
}

# Build lookup: id -> yc
$ycOf = @{}
foreach ($e in $d.elements) {
  $a = $nodeById[$e.n1]; $b = $nodeById[$e.n2]; $c = $nodeById[$e.n3]
  $ycOf[$e.id] = ($a.y + $b.y + $c.y) / 3
}

# Compare σyy by depth band among: cavern elements (future stage-2 cut) vs surrounding solid.
$ymid_low  = [double]([Math]::Round((($ycOf[[int]($caveIds.Keys | Select-Object -First 1)]) - 2)))
foreach ($b in @(44..20)) {} # no-op

# Pick a y-band straddling the cavern and sample surroundings at same depth.
# For each element we classify: cavern / tunnel / outside.
$caveYs = @(); $outsideByBand = @{}
$tunYs  = @()
foreach ($es in $stage1.element_stresses) {
  $eid = $es.id
  $yc  = $ycOf[$eid]
  if ($caveIds.ContainsKey($eid)) { $caveYs += ,@($yc, $es.syy) }
  elseif ($tunIds.ContainsKey($eid)) { $tunYs += ,@($yc, $es.syy) }
  else {
    $band = [int][Math]::Floor($yc)
    if (-not $outsideByBand.ContainsKey($band)) { $outsideByBand[$band] = New-Object System.Collections.ArrayList }
    [void]$outsideByBand[$band].Add($es.syy)
  }
}

# Summary of cavern band
if ($caveYs.Count -gt 0) {
  $cYmin = ($caveYs | ForEach-Object { $_[0] } | Measure-Object -Min).Minimum
  $cYmax = ($caveYs | ForEach-Object { $_[0] } | Measure-Object -Max).Maximum
  $cSyyMin = ($caveYs | ForEach-Object { $_[1] } | Measure-Object -Min).Minimum
  $cSyyMax = ($caveYs | ForEach-Object { $_[1] } | Measure-Object -Max).Maximum
  $cSyyAvg = ($caveYs | ForEach-Object { $_[1] } | Measure-Object -Average).Average
  "cavern (future stage-2 cut) N={0} y:[{1:F2},{2:F2}]  syy min={3:E3} max={4:E3} avg={5:E3}" -f $caveYs.Count, $cYmin, $cYmax, $cSyyMin, $cSyyMax, $cSyyAvg

  # Compare to surroundings at same depth band
  for ($b = [int][Math]::Floor($cYmin); $b -le [int][Math]::Ceiling($cYmax); ++$b) {
    if ($outsideByBand.ContainsKey($b) -and $outsideByBand[$b].Count -gt 0) {
      $avg = ($outsideByBand[$b] | Measure-Object -Average).Average
      $min = ($outsideByBand[$b] | Measure-Object -Min).Minimum
      $max = ($outsideByBand[$b] | Measure-Object -Max).Maximum
      "  band y in [{0},{1})  outside elements N={2}  syy avg={3:E3}  min={4:E3}  max={5:E3}" -f $b, ($b+1), $outsideByBand[$b].Count, $avg, $min, $max
    }
  }

  # Cavern average syy by y band for same depth comparison
  $caveByBand = @{}
  foreach ($p in $caveYs) {
    $band = [int][Math]::Floor($p[0])
    if (-not $caveByBand.ContainsKey($band)) { $caveByBand[$band] = New-Object System.Collections.ArrayList }
    [void]$caveByBand[$band].Add($p[1])
  }
  "cavern syy by y-band:"
  foreach ($b in ($caveByBand.Keys | Sort-Object)) {
    $avg = ($caveByBand[$b] | Measure-Object -Average).Average
    $min = ($caveByBand[$b] | Measure-Object -Min).Minimum
    $max = ($caveByBand[$b] | Measure-Object -Max).Maximum
    "  cave  band y in [{0},{1})  N={2}  syy avg={3:E3}  min={4:E3}  max={5:E3}" -f $b, ($b+1), $caveByBand[$b].Count, $avg, $min, $max
  }
}
