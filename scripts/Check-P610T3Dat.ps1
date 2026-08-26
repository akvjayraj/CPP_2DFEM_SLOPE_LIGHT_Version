# Scan a p610_t3 .dat file for suspicious geometry / connectivity.
#   .\scripts\Check-P610T3Dat.ps1  <path>
param([Parameter(Mandatory)] [string] $Path)

$tokens = New-Object System.Collections.Generic.List[string]
foreach ($line in Get-Content -LiteralPath $Path) {
    $i = $line.IndexOf('!')
    if ($i -ge 0) { $line = $line.Substring(0, $i) }
    $line = $line.Trim()
    if ($line.Length -eq 0) { continue }
    foreach ($t in ($line -split '\s+')) { if ($t) { $tokens.Add($t) } }
}

$pos = 0
function Next-Int   { $v = [int]$script:tokens[$script:pos]; $script:pos++; return $v }
function Next-Float { $v = [double]$script:tokens[$script:pos]; $script:pos++; return $v }

$nels    = Next-Int
$nn      = Next-Int
$nptypes = Next-Int
"nels={0}  nn={1}  np_types={2}" -f $nels, $nn, $nptypes

for ($k = 0; $k -lt (7 * $nptypes); $k++) { [void] (Next-Float) }
if ($nptypes -gt 1) { for ($k = 0; $k -lt $nels; $k++) { [void] (Next-Int) } }

# Flat arrays to avoid PowerShell 2D-indexing parse issues
$cx = New-Object 'double[]' $nn
$cy = New-Object 'double[]' $nn
for ($k = 0; $k -lt $nn; $k++) { $cx[$k] = Next-Float; $cy[$k] = Next-Float }

$na = New-Object 'int[]' $nels
$nb = New-Object 'int[]' $nels
$nc = New-Object 'int[]' $nels
for ($k = 0; $k -lt $nels; $k++) { $na[$k] = Next-Int; $nb[$k] = Next-Int; $nc[$k] = Next-Int }

$fillerZero = 0
for ($k = 0; $k -lt $nn; $k++) { if ($cx[$k] -eq 0 -and $cy[$k] -eq 0) { $fillerZero++ } }
"coords at (0,0): $fillerZero"

$ref = New-Object 'System.Collections.Generic.HashSet[int]'
for ($k = 0; $k -lt $nels; $k++) {
    [void] $ref.Add($na[$k])
    [void] $ref.Add($nb[$k])
    [void] $ref.Add($nc[$k])
}

$zero = New-Object System.Collections.Generic.List[pscustomobject]
$tiny = New-Object System.Collections.Generic.List[pscustomobject]
$neg  = New-Object System.Collections.Generic.List[pscustomobject]
$dup  = New-Object System.Collections.Generic.List[pscustomobject]
$bad  = New-Object System.Collections.Generic.List[pscustomobject]

for ($k = 0; $k -lt $nels; $k++) {
    $a = $na[$k]; $b = $nb[$k]; $c = $nc[$k]
    if ($a -lt 1 -or $b -lt 1 -or $c -lt 1 -or $a -gt $nn -or $b -gt $nn -or $c -gt $nn) {
        $bad.Add([pscustomobject]@{ Elem = $k + 1; Nodes = "$a $b $c" }); continue
    }
    if ($a -eq $b -or $b -eq $c -or $a -eq $c) {
        $dup.Add([pscustomobject]@{ Elem = $k + 1; Nodes = "$a $b $c" }); continue
    }
    $x1 = $cx[$a - 1]; $y1 = $cy[$a - 1]
    $x2 = $cx[$b - 1]; $y2 = $cy[$b - 1]
    $x3 = $cx[$c - 1]; $y3 = $cy[$c - 1]
    $area = 0.5 * (($x2 - $x1) * ($y3 - $y1) - ($x3 - $x1) * ($y2 - $y1))
    if ([Math]::Abs($area) -lt 1e-12) { $zero.Add([pscustomobject]@{ Elem = $k + 1; Area = $area; Nodes = "$a $b $c" }) }
    elseif ([Math]::Abs($area) -lt 1e-6) { $tiny.Add([pscustomobject]@{ Elem = $k + 1; Area = $area; Nodes = "$a $b $c" }) }
    elseif ($area -lt 0) { $neg.Add([pscustomobject]@{ Elem = $k + 1; Area = $area; Nodes = "$a $b $c" }) }
}

"out-of-range node refs  : {0}" -f $bad.Count
if ($bad.Count)  { $bad  | Select-Object -First 5 | Format-Table -AutoSize }
"duplicate-node elements : {0}" -f $dup.Count
if ($dup.Count)  { $dup  | Select-Object -First 5 | Format-Table -AutoSize }
"zero-area  (<1e-12)     : {0}" -f $zero.Count
if ($zero.Count) { $zero | Select-Object -First 5 | Format-Table -AutoSize }
"tiny-area  (<1e-6)      : {0}" -f $tiny.Count
if ($tiny.Count) { $tiny | Select-Object -First 5 | Format-Table -AutoSize }
"negative-area elements  : {0}" -f $neg.Count
if ($neg.Count)  { $neg  | Select-Object -First 5 | Format-Table -AutoSize }

$unused = 1..$nn | Where-Object { -not $ref.Contains($_) }
"unused node ids         : {0}" -f @($unused).Count
if (@($unused).Count) { "  sample: " + ((@($unused) | Select-Object -First 10) -join ', ') }
