$d = Get-Content -Raw -Path sample.json | ConvertFrom-Json
$nodeById = @{}
foreach ($n in $d.nodes) { $nodeById[$n.id] = $n }
$elById = @{}
foreach ($e in $d.elements) { $elById[$e.id] = $e }

# Stage 2 excavated elements = later-cavern + stage-1 tunnel
$excAll = @{}
foreach ($e in $d.elements) { if ($e.active -eq 0) { $excAll[$e.id] = $true } }

# Stage 1 excavated (tunnel) from stages[1].element_stresses — tunnel elements
# would have ~0 tensor there. Everything else is solid at stage 1.
$stage1 = $d.stages | Where-Object { $_.index -eq 1 } | Select-Object -First 1
$stage2 = $d.stages | Where-Object { $_.index -eq 2 } | Select-Object -First 1

$s1zero = @(); $s1nz = @()
foreach ($es in $stage1.element_stresses) {
  $mag = [Math]::Abs($es.sxx) + [Math]::Abs($es.syy) + [Math]::Abs($es.txy) + [Math]::Abs($es.szz)
  if ($mag -lt 1e-20) { $s1zero += $es.id } else { $s1nz += $es.id }
}
"stage 1: {0} elements with all-zero stress (flagged as void by viewer), {1} non-zero" -f $s1zero.Count, $s1nz.Count

# Of the stage-1 zeroed ones, how many are also in $excAll (cavern, stage-2 cut)?
$tunnel_s1 = 0; $cavern_s1_void = @()
foreach ($id in $s1zero) {
  if ($excAll.ContainsKey($id)) {
    # Determine by yc if it's cavern or tunnel
    $e = $elById[$id]
    $yc = ($nodeById[$e.n1].y + $nodeById[$e.n2].y + $nodeById[$e.n3].y)/3
    if ($yc -gt 30) { $cavern_s1_void += ,@($id, $yc) }   # upper half → cavern
    else            { $tunnel_s1++ }
  } else {
    # outside cavern+tunnel but all-zero at stage 1?
    "unexpected: element id=$id has 0 stress at stage 1 but is not in excavated list"
  }
}
"  tunnel void at stage 1: $tunnel_s1 (expected - these are the stage-1 cuts)"
"  cavern (stage-2 cut) with 0 stress at STAGE 1: $($cavern_s1_void.Count)  <-- should be 0 because cavern is not yet cut"
$cavern_s1_void | Select-Object -First 10 | ForEach-Object { "    id={0}  yc={1:F2}" -f $_[0], $_[1] }

# σyy global range for stage 1
$syy = $stage1.element_stresses | ForEach-Object { $_.syy }
$vmin = ($syy | Measure-Object -Min).Minimum
$vmax = ($syy | Measure-Object -Max).Maximum
"stage 1 syy range: [{0:E3}, {1:E3}]" -f $vmin, $vmax
