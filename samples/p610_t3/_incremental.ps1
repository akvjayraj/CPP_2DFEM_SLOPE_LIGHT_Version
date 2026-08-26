param([string]$Path = "sample.json")
$raw = Get-Content -Raw -Path $Path
$d   = $raw | ConvertFrom-Json
$prev = $null
foreach ($s in $d.stages) {
  if ($null -ne $prev) {
    $mxInc = 0.0; $mxTot = 0.0
    for ($i = 0; $i -lt $s.nodes.Count; ++$i) {
      $a = $s.nodes[$i]; $b = $prev.nodes[$i]
      $dx = $a.ux - $b.ux; $dy = $a.uy - $b.uy
      $inc = [Math]::Sqrt($dx*$dx + $dy*$dy)
      $tot = [Math]::Sqrt($a.ux*$a.ux + $a.uy*$a.uy)
      if ($inc -gt $mxInc) { $mxInc = $inc }
      if ($tot -gt $mxTot) { $mxTot = $tot }
    }
    "stage {0,2}->{1,-2}   max incremental du={2:E4} m   max cumulative u={3:E4} m" -f $prev.index, $s.index, $mxInc, $mxTot
  }
  $prev = $s
}
