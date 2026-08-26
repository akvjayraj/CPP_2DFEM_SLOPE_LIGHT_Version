# Re-bracket Example 16 wet FoS from existing trial JSON meta using the
# yield-ratio gate (max_yield_ratio <= 25), then refresh gui_automation summaries.
# Optionally runs missing mid-bracket trials via p610_t3_elastoplastic.exe.
param(
  [string]$TrialDir = "$PSScriptRoot\gui_automation\t3",
  [double]$YieldGate = 25.0,
  [double]$LiteratureFoS = 1.02,
  [double]$Tolerance = 0.01,
  [switch]$RunMissingTrials,
  [string]$SolverExe = ""
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
  if ($brace -lt 0) { return $null }
  $depth = 0
  $end = -1
  for ($i = $brace; $i -lt $head.Length; $i++) {
    $ch = $head[$i]
    if ($ch -eq '{') { $depth++ }
    elseif ($ch -eq '}') {
      $depth--
      if ($depth -eq 0) { $end = $i; break }
    }
  }
  if ($end -lt 0) { return $null }
  $block = $head.Substring($brace, $end - $brace + 1)

  $f = $null; $yld = $null; $iters = $null; $disp = $null; $all = $null
  if ($block -match '"F_trial"\s*:\s*([-+eE0-9.]+)') { $f = [double]::Parse($Matches[1], $ci) }
  if ($block -match '"max_yield_ratio"\s*:\s*([-+eE0-9.]+)') { $yld = [double]::Parse($Matches[1], $ci) }
  if ($block -match '"max_iters_used"\s*:\s*([-+eE0-9.]+)') { $iters = [int][Math]::Round([double]::Parse($Matches[1], $ci)) }
  if ($block -match '"max_disp"\s*:\s*([-+eE0-9.]+)') { $disp = [double]::Parse($Matches[1], $ci) }
  if ($block -match '"all_increments_converged"\s*:\s*(true|false)') { $all = $Matches[1] -eq 'true' }
  if ($null -eq $f) { return $null }
  [pscustomobject]@{
    Path = $path
    F = $f
    Yield = $yld
    Iters = $iters
    MaxDisp = $disp
    AllIncsConverged = $all
  }
}

function Test-Converged($meta, [double]$gate) {
  if ($null -eq $meta) { return $false }
  if ($null -eq $meta.Iters -or $meta.Iters -ge 1000) { return $false }
  if ($meta.AllIncsConverged -eq $false) { return $false }
  if ($null -eq $meta.MaxDisp) { return $false }
  $disp = [double]$meta.MaxDisp
  if ([double]::IsNaN($disp) -or [double]::IsInfinity($disp)) { return $false }
  if ($null -ne $meta.Yield -and $meta.Yield -gt $gate) { return $false }
  return $true
}

if (-not (Test-Path -LiteralPath $TrialDir)) {
  throw "Trial directory not found: $TrialDir"
}

$trials = @()
Get-ChildItem -LiteralPath $TrialDir -Filter 'p610t3_script_run_srf_*.json' |
  Where-Object { $_.Name -notmatch '_dry' } |
  ForEach-Object {
    $m = Read-SrfMeta $_.FullName
    if ($m) {
      $ok = Test-Converged $m $YieldGate
      $trials += [pscustomobject]@{
        F = $m.F
        Converged = $ok
        Yield = $m.Yield
        Iters = $m.Iters
        MaxDisp = $m.MaxDisp
        AllIncs = $m.AllIncsConverged
        File = $_.Name
      }
    }
  }

$trials = @($trials | Sort-Object F)
# Deduplicate by F (keep last)
$byF = [ordered]@{}
foreach ($t in $trials) { $byF[[string]$t.F] = $t }
$trials = @($byF.Values | Sort-Object { $_.F })

if ($trials.Count -eq 0) { throw "No wet trial JSON meta found in $TrialDir" }

Write-Host "Wet trials (yield gate <= $YieldGate):"
$trials | Format-Table F, Converged, Yield, Iters, MaxDisp, File -AutoSize | Out-String | Write-Host

$passed = @($trials | Where-Object Converged | Sort-Object F)
$failed = @($trials | Where-Object { -not $_.Converged } | Sort-Object F)

if ($passed.Count -eq 0) {
  throw "No stable wet trials under yield gate $YieldGate. Rebuild solver / re-run near F=0.9."
}
if ($failed.Count -eq 0) {
  throw "No failed wet trials under yield gate $YieldGate. Cannot form a bracket."
}

$fLo = ($passed | Select-Object -Last 1).F
$fHiCandidates = @($failed | Where-Object { $_.F -gt $fLo } | Sort-Object F)
if ($fHiCandidates.Count -eq 0) {
  throw "No failed trial above highest stable F=$fLo. Need an upper bracket point."
}
$fHi = $fHiCandidates[0].F

Write-Host "Initial bracket from artifacts: F_lo=$fLo  F_hi=$fHi"

if ($RunMissingTrials) {
  if (-not $SolverExe) {
    $repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
    $candidates = @(
      (Join-Path $repo 'build\Debug\p610_t3_elastoplastic.exe'),
      (Join-Path $repo 'build\Release\p610_t3_elastoplastic.exe'),
      (Join-Path $repo 'X2D_GUI\bin\Release\net8.0-windows\p610_t3_elastoplastic.exe'),
      (Join-Path $repo 'X2D_GUI\bin\Debug\net8.0-windows\p610_t3_elastoplastic.exe')
    )
    $SolverExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
  }
  if (-not $SolverExe -or -not (Test-Path $SolverExe)) {
    Write-Warning "Solver exe not found; skipping live bisection."
  } else {
    $SolverExe = (Resolve-Path -LiteralPath $SolverExe).Path
    $template = Join-Path $TrialDir 'p610t3_script_run_srf_1.dat'
    if (-not (Test-Path $template)) { throw "Missing template deck: $template" }
    $work = Join-Path $TrialDir 'rebracket_work'
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $guard = 0
    while (($fHi - $fLo) -gt $Tolerance -and $guard -lt 8) {
      $guard++
      $mid = [Math]::Round(($fLo + $fHi) / 2.0, 4)
      $existing = $trials | Where-Object { [Math]::Abs($_.F - $mid) -lt 1e-4 } | Select-Object -First 1
      if ($existing) {
        if ($existing.Converged) { $fLo = $mid } else { $fHi = $mid }
        Write-Host "Reuse F=$mid converged=$($existing.Converged) -> [$fLo, $fHi]"
        continue
      }
      $tag = ($mid.ToString('G4', $ci)).Replace('.', '_')
      $baseName = "p610t3_script_run_srf_$tag"
      $datOut = Join-Path $work "$baseName.dat"
      $content = Get-Content -LiteralPath $template -Raw
      $content = [regex]::Replace($content, '(?m)^(!\s*srf_F\s+)\S+', "`${1}$($mid.ToString($ci))")
      $content = [regex]::Replace($content, '(?m)^(1E-05\s+1000\s+)\d+(\s+1\s*)$', "`${1}100`${2}")
      Set-Content -LiteralPath $datOut -Value $content -NoNewline
      Write-Host "Running solver at F=$mid with $SolverExe ..."
      Push-Location $work
      try {
        # p610_t3 expects argv[1] = base name without .dat
        & $SolverExe $baseName *>&1 | Tee-Object -FilePath (Join-Path $work "$baseName.log") | Out-Null
      } finally { Pop-Location }
      $json = Join-Path $work "$baseName.json"
      if (-not (Test-Path $json)) {
        $alt = Get-ChildItem $work -Filter '*.json' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($alt) {
          Copy-Item $alt.FullName $json -Force
        }
      }
      if (Test-Path $json) {
        Copy-Item $json (Join-Path $TrialDir (Split-Path $json -Leaf)) -Force
        $datCopy = Join-Path $TrialDir "$baseName.dat"
        Copy-Item $datOut $datCopy -Force
        $m = Read-SrfMeta $json
        $ok = Test-Converged $m $YieldGate
        Write-Host "  F=$mid converged=$ok yield=$($m.Yield) iters=$($m.Iters)"
        if ($ok) { $fLo = $mid } else { $fHi = $mid }
        $trials += [pscustomobject]@{
          F = $mid; Converged = $ok; Yield = $m.Yield; Iters = $m.Iters
          MaxDisp = $m.MaxDisp; AllIncs = $m.AllIncsConverged; File = (Split-Path $json -Leaf)
        }
      } else {
        Write-Warning "No JSON for F=$mid; stopping live bisection."
        break
      }
    }
  }
}

$estimated = ($fLo + $fHi) / 2.0
$dev = $estimated - $LiteratureFoS
$devPct = 100.0 * $dev / $LiteratureFoS
$status = "Critical FoS ~ $($estimated.ToString('F3',$ci)) (yield-gate bracket F in [$($fLo.ToString('G4',$ci)), $($fHi.ToString('G4',$ci))], gate=$YieldGate)"

$summaryPath = Join-Path $PSScriptRoot 'gui_automation\rs2_example16_gui_run_summary.json'
$mdPath = Join-Path $PSScriptRoot 'gui_automation\rs2_example16_gui_run_summary.md'
$casePath = Join-Path $TrialDir 'rs2_example16_t3_result.json'

$nodeCount = 8618
$elemCount = 14725
if (Test-Path $summaryPath) {
  try {
    $old = Get-Content $summaryPath -Raw | ConvertFrom-Json
    $c0 = @($old.Cases) | Select-Object -First 1
    if ($c0.NodeCount) { $nodeCount = [int]$c0.NodeCount }
    if ($c0.ElementCount) { $elemCount = [int]$c0.ElementCount }
  } catch {}
}

$trialRows = @($trials | Sort-Object F | ForEach-Object {
  [ordered]@{
    Srf = $_.F
    MaxDisplacement = $_.MaxDisp
    Iterations = $_.Iters
    Converged = [bool]$_.Converged
    MaxYieldRatio = $_.Yield
  }
})

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot 'rs2_example16_layered_slope_water_table_weak_seam.xfem')).Path
$case = [ordered]@{
  MeshType = 'T3'
  ProjectPath = $projectPath
  Program = 'slope_stability_T3_SRF'
  BrandedProgram = 'slope_stability_T3_SRF'
  SolverCore = 'CoreA'
  DeckFormat = 'P610'
  ScriptSyncedFromMesh = $true
  GroundwaterEnabled = $true
  NonHorizontalLayerCutCount = 3
  EstimatedFoS = $estimated
  DeviationFromLiterature = $dev
  DeviationPercent = $devPct
  StatusMessage = $status
  FosNote = "Re-bracketed with max_yield_ratio <= $YieldGate (false-stable iters rejected)."
  ArtifactSummary = "Yield-gate rebracket from gui_automation/t3 trial JSON meta."
  MeshRowCount = $elemCount
  NodeCount = $nodeCount
  ElementCount = $elemCount
  ArtifactDirectory = (Resolve-Path $TrialDir).Path
  TrialRows = $trialRows
  YieldGate = $YieldGate
  BracketLo = $fLo
  BracketHi = $fHi
}

$summary = [ordered]@{
  GeneratedAt = (Get-Date).ToString('o')
  LiteratureFoS = $LiteratureFoS
  ReferenceFoSBishop = 1.087
  ReferenceFoSSpencerCircular = 1.093
  ReferenceFoSSpencerNonCircular = 1.007
  YieldGate = $YieldGate
  Cases = @($case)
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$case | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $casePath -Encoding UTF8

$md = @"
# RS2 Example 16 (Greco 1996 / Chen & Shao 1988) GUI Automation Summary

Generated: ``$($summary.GeneratedAt)``
RS2 SSR reference FoS: ``$($LiteratureFoS.ToString('F2',$ci))``
Bishop (circular) reference: ``1.087``
Spencer (circular) reference: ``1.093``
Spencer (non-circular) reference: ``1.007``
Yield gate: ``max_yield_ratio <= $($YieldGate.ToString('G',$ci))``

| Mesh | FoS | Deviation | Deviation % | Nodes | Elements | GW |
|---|---:|---:|---:|---:|---:|:---:|
| T3 | $($estimated.ToString('F3',$ci)) | $($dev.ToString('F3',$ci)) | $($devPct.ToString('F3',$ci)) | $nodeCount | $elemCount | yes |

$status

Bracket: F ∈ [$($fLo.ToString('G4',$ci)), $($fHi.ToString('G4',$ci))]
"@
Set-Content -LiteralPath $mdPath -Value $md -Encoding UTF8

$cmpPath = Join-Path $PSScriptRoot 'comparision.txt'
@"
T3 mesh (yield-gate rebracket $($summary.GeneratedAt))
only SRF (dry hand note):1.25
With SRF+load:1.22
with SRF+ water:$($estimated.ToString('F2',$ci))
with water + load+ SRF:0.85
with SRF + Seismic= 0.59 Kh=0.4
with SRF + Seismic= 0.22 Kh=0.5
literature RS2 SSR:1.02
"@ | Set-Content -LiteralPath $cmpPath -Encoding UTF8

Write-Host ""
Write-Host $status
Write-Host "Deviation from literature 1.02: $($devPct.ToString('F2',$ci))%"
Write-Host "Wrote $summaryPath"
Write-Host "Wrote $mdPath"
Write-Host "Wrote $casePath"
Write-Host "Wrote $cmpPath"
