# Confirm dry (GW off) F=1 / F=0.5 convergence for Example 16 after gravity-floor fix.
param(
  [string]$TrialDir = "$PSScriptRoot\gui_automation\t3",
  [string]$SolverExe = "",
  [double[]]$Factors = @(1.0, 0.5),
  [double]$YieldGate = 25.0
)
$ErrorActionPreference = 'Stop'
$ci = [Globalization.CultureInfo]::InvariantCulture

function Read-SrfMeta([string]$path) {
  $fs = [IO.File]::OpenRead($path)
  try {
    $buf = New-Object byte[] 16384
    $n = $fs.Read($buf, 0, $buf.Length)
    $head = [Text.Encoding]::UTF8.GetString($buf, 0, $n)
  } finally { $fs.Dispose() }
  if ($head -notmatch '"srf"\s*:\s*\{') { return $null }
  $start = $head.IndexOf('"srf"')
  $brace = $head.IndexOf('{', $start)
  $depth = 0; $end = -1
  for ($i = $brace; $i -lt $head.Length; $i++) {
    if ($head[$i] -eq '{') { $depth++ }
    elseif ($head[$i] -eq '}') { $depth--; if ($depth -eq 0) { $end = $i; break } }
  }
  if ($end -lt 0) { return $null }
  $block = $head.Substring($brace, $end - $brace + 1)
  $o = [pscustomobject]@{ F=$null; Yield=$null; Iters=$null; MaxDisp=$null; AllIncs=$null }
  if ($block -match '"F_trial"\s*:\s*([-+eE0-9.]+)') { $o.F = [double]::Parse($Matches[1], $ci) }
  if ($block -match '"max_yield_ratio"\s*:\s*([-+eE0-9.]+)') { $o.Yield = [double]::Parse($Matches[1], $ci) }
  if ($block -match '"max_iters_used"\s*:\s*([-+eE0-9.]+)') { $o.Iters = [int][Math]::Round([double]::Parse($Matches[1], $ci)) }
  if ($block -match '"max_disp"\s*:\s*([-+eE0-9.]+)') { $o.MaxDisp = [double]::Parse($Matches[1], $ci) }
  if ($block -match '"all_increments_converged"\s*:\s*(true|false)') { $o.AllIncs = $Matches[1] -eq 'true' }
  return $o
}

function Test-Ok($m) {
  if ($null -eq $m) { return $false }
  if ($null -eq $m.Iters -or $m.Iters -ge 1000) { return $false }
  if ($m.AllIncs -eq $false) { return $false }
  if ($null -ne $m.Yield -and $m.Yield -gt $YieldGate) { return $false }
  return $true
}

if (-not $SolverExe) {
  $repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
  $SolverExe = @(
    "$repo\build\Debug\p610_t3_elastoplastic.exe",
    "$repo\build\Release\p610_t3_elastoplastic.exe",
    "$repo\X2D_GUI\bin\Release\net8.0-windows\p610_t3_elastoplastic.exe",
    "$repo\X2D_GUI\bin\Debug\net8.0-windows\p610_t3_elastoplastic.exe"
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $SolverExe) { throw "p610_t3_elastoplastic.exe not found. Build the solver first." }
$SolverExe = (Resolve-Path -LiteralPath $SolverExe).Path

$templateWet = Join-Path $TrialDir 'p610t3_script_run_srf_1.dat'
if (-not (Test-Path $templateWet)) { throw "Missing $templateWet" }

$work = Join-Path $TrialDir 'dry_baseline_work'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$results = @()

foreach ($f in $Factors) {
  $tag = ($f.ToString('G4', $ci)).Replace('.', '_')
  $base = "p610t3_script_run_srf_${tag}_dry_incs100"
  $dat = Join-Path $work "$base.dat"
  $content = Get-Content -LiteralPath $templateWet -Raw
  $content = [regex]::Replace($content, '(?m)^(!\s*include\s+)\S+', '${1}false')
  $content = [regex]::Replace($content, '(?m)^(!\s*srf_F\s+)\S+', "`${1}$($f.ToString($ci))")
  $content = [regex]::Replace($content, '(?m)^(1E-05\s+1000\s+)\d+(\s+1\s*)$', "`${1}100`${2}")
  Set-Content -LiteralPath $dat -Value $content -NoNewline

  Write-Host "=== Dry F=$f ==="
  Push-Location $work
  try {
    # p610_t3 expects argv[1] = base name without .dat
    & $SolverExe $base *>&1 | Tee-Object -FilePath (Join-Path $work "$base.log") | Out-Null
  } finally { Pop-Location }

  $json = Join-Path $work "$base.json"
  if (-not (Test-Path $json)) {
    $alt = Get-ChildItem $work -Filter '*.json' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($alt) { Copy-Item $alt.FullName $json -Force }
  }
  if (Test-Path $json) {
    Copy-Item $json (Join-Path $TrialDir (Split-Path $json -Leaf)) -Force
    Copy-Item $dat (Join-Path $TrialDir (Split-Path $dat -Leaf)) -Force
    $m = Read-SrfMeta $json
    $ok = Test-Ok $m
    $results += [pscustomobject]@{ F=$f; Converged=$ok; Yield=$m.Yield; Iters=$m.Iters; MaxDisp=$m.MaxDisp; File=(Split-Path $json -Leaf) }
    Write-Host "  converged=$ok yield=$($m.Yield) iters=$($m.Iters) |u|max=$($m.MaxDisp)"
  } else {
    $results += [pscustomobject]@{ F=$f; Converged=$false; Yield=$null; Iters=$null; MaxDisp=$null; File='(missing json)' }
    Write-Warning "No JSON produced for dry F=$f"
  }
}

$outMd = Join-Path $PSScriptRoot 'gui_automation\dry_baseline_summary.md'
$lines = @(
  '# Example 16 dry baseline',
  '',
  "Generated: ``$((Get-Date).ToString('o'))``",
  "Solver: ``$SolverExe``",
  "Gravity deck incs: 100 (solver floor also 100)",
  "Yield gate: $YieldGate",
  '',
  '| F | Converged | Yield | Iters | max\|u\| | File |',
  '|---:|:---:|---:|---:|---:|---|'
)
foreach ($r in $results) {
  $lines += "| $($r.F.ToString('G',$ci)) | $($r.Converged) | $($r.Yield) | $($r.Iters) | $($r.MaxDisp) | $($r.File) |"
}
$f1 = $results | Where-Object { [Math]::Abs($_.F - 1.0) -lt 1e-9 } | Select-Object -First 1
if ($f1 -and $f1.Converged) {
  $lines += ''
  $lines += '**Dry F=1 converges** under the yield-ratio gate — dry baseline OK before trusting wet FoS.'
} else {
  $lines += ''
  $lines += '**Dry F=1 did not converge** under the gate. Prefer T6 / finer seam mesh before claiming wet FoS vs literature 1.02.'
}
$lines -join "`n" | Set-Content -LiteralPath $outMd -Encoding UTF8
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'gui_automation\dry_baseline_summary.json') -Encoding UTF8
Write-Host "Wrote $outMd"
