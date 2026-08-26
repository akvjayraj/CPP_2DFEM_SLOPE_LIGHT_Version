param([string]$Path = "sample.json")
$raw = Get-Content -Raw -Path $Path
$d = $raw | ConvertFrom-Json
foreach ($s in $d.stages) {
  $mx = 0.0; $mxx = 0.0; $myy = 0.0
  foreach ($n in $s.nodes) {
    $u = [Math]::Sqrt($n.ux*$n.ux + $n.uy*$n.uy)
    if ($u  -gt $mx)  { $mx  = $u }
    if ([Math]::Abs($n.ux) -gt $mxx) { $mxx = [Math]::Abs($n.ux) }
    if ([Math]::Abs($n.uy) -gt $myy) { $myy = [Math]::Abs($n.uy) }
  }
  "stage {0,2} {1,-32}  max|u|={2:E4}  max|ux|={3:E4}  max|uy|={4:E4}" -f $s.index, $s.name, $mx, $mxx, $myy
}
