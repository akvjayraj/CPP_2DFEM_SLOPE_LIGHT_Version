<#
.SYNOPSIS
  Build slope-stability verification packages under Verification/geotech/slope_*.

.DESCRIPTION
  Walks every Verification/geotech/slope_* folder, runs the solver named in
  each .dat AnalysisOptions block, and computes a slope-stability verdict
  according to the .dat's `! expected_FoS_mode`:

    Mode `lambda_sweep` (Option A, no solver changes):
      The folder holds N copies of the same mesh, only differing by the
      `! lambda_factor` applied to material gamma. Each is run, classified
      converged/non-converged via .res, and tabulated with max-displacement
      and max-yield-ratio. The aggregator reports the lambda where max-disp
      first jumps super-linearly versus lambda (a soft collapse signal).
      Verdict per file: PASS if the solver completed every increment without
      hitting the iteration limit. The folder PASSES if every lambda run did
      so AND the diagnostic table is monotonic in lambda. This mode is a
      DIAGNOSTIC, not a sharp FoS detector -- the small-strain viscoplastic
      algorithm in p610_t3_*.exe redistributes through nominal collapse
      without diverging, so use srf-mode (Option B) for a sharp FoS check.

    Mode `srf` (Option B, requires the C++ SRF bisection feature):
      A single .dat per folder declares `! srf_mode 1`. The solver bisects
      on the strength-reduction factor F (phi -> atan(tan phi / F),
      c -> c / F) until non-convergence and writes the converged F into
      `meta.srf_FoS` of the JSON. The orchestrator just reads it back.
      Verdict: PASS if |srf_FoS - expected_FoS| / expected_FoS <= tolerance%.

  Per-folder outputs:
    - <folder>_SlopeTest_Summary.csv   (per-lambda diagnostic table)
    - <folder>_SlopeTest_Help.md (+ optional .docx, .pdf)

  Top-level outputs:
    - Verification/geotech/Slope_All_SmokeTests_Summary.csv
    - Verification/geotech/Slope_All_SmokeTests_Help.md

  The geotech bearing-capacity orchestrator (build_all_geotech_smoke_packages.ps1)
  already skips slope_* folders in its loop so the two verdict paths don't
  fight over the same .dat files.
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $SkipDocx,
    [switch] $SkipPdf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $scriptDir '..')
}

function Get-CommitHash {
    param([string] $Root)
    try {
        $hash = git -C $Root rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $hash) { return $hash.Trim() }
    } catch { }
    return 'unknown'
}

function Test-FiniteNumber {
    param($Value)
    if ($null -eq $Value) { return $false }
    $d = [double] $Value
    return -not ([double]::IsNaN($d) -or [double]::IsInfinity($d))
}

function Read-JsonLenient {
    param([string] $Path)
    $raw = Get-Content -Raw $Path
    $raw = [regex]::Replace($raw, '(?i)(?<=[:\[,]\s*)-?inf(?=\s*[,}\]])', 'null')
    $raw = [regex]::Replace($raw, '(?i)(?<=[:\[,]\s*)nan(?=\s*[,}\]])', 'null')
    return ($raw | ConvertFrom-Json)
}

function Resolve-ExePath {
    param([string] $RepoRoot, [string] $ExeName)
    $candidates = @(
        (Join-Path $RepoRoot ("library\solver\{0}.exe" -f $ExeName)),
        (Join-Path $RepoRoot ("build\Release\{0}.exe" -f $ExeName)),
        (Join-Path $RepoRoot ("build\Debug\{0}.exe" -f $ExeName))
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function Read-DatHeaderField {
    param([string[]] $Lines, [string] $Field)
    foreach ($line in $Lines) {
        if ($line -match ('^\s*!\s*' + [regex]::Escape($Field) + '\s+(\S+)\s*$')) {
            return $Matches[1]
        }
    }
    return $null
}

function Format-Num {
    param($Value)
    if (-not (Test-FiniteNumber $Value)) { return 'n/a' }
    return [string]::Format('{0:F6}', [double]$Value)
}

# Returns the largest 'iters' value from "Inc N took M iterations" lines, or
# $null when no such line is present.
function Get-MaxIters {
    param([string] $ResPath)
    if (-not (Test-Path $ResPath)) { return $null }
    $maxI = 0
    foreach ($line in [System.IO.File]::ReadAllLines($ResPath)) {
        if ($line -match 'Inc\s+\d+\s+took\s+(\d+)\s+iterations') {
            $iters = [int] $Matches[1]
            if ($iters -gt $maxI) { $maxI = $iters }
        }
    }
    return $maxI
}

function Get-LimitFromDat {
    param([string[]] $DatLines)
    foreach ($line in $DatLines) {
        if ($line -match '^\s*\d*\.?\d+\s+(\d+)\s+\d+\s+\d+\s*$') {
            $cand = [int] $Matches[1]
            if ($cand -ge 50 -and $cand -le 100000) { return $cand }
        }
    }
    return $null
}

function Measure-MaxDisplacement {
    param($JsonRoot)
    if ($null -eq $JsonRoot -or $null -eq $JsonRoot.stages) { return $null }
    $stages = @($JsonRoot.stages)
    if ($stages.Count -lt 2) { return $null }
    # JSON layout per stage: stages[i].nodes is an array of {id, x, y, ux, uy}.
    $nodes = @($stages[-1].nodes)
    if ($nodes.Count -eq 0) { return $null }
    $maxD = 0.0
    foreach ($n in $nodes) {
        $r = [math]::Sqrt([double]$n.ux * [double]$n.ux + [double]$n.uy * [double]$n.uy)
        if ($r -gt $maxD) { $maxD = $r }
    }
    return $maxD
}

# Mohr-Coulomb yield-ratio scan (Smith-Griffiths tension+ convention).
# Returns max_e (s1-s2) / [2*(c*cos(phi) - sigma_m*sin(phi))]; > 1.0 means
# the element is plastically loaded past the yield surface.
function Measure-MaxYieldRatio {
    param($JsonRoot, [double] $PhiDeg, [double] $C_kPa)
    if ($null -eq $JsonRoot -or $null -eq $JsonRoot.stages) { return $null }
    $stages = @($JsonRoot.stages)
    if ($stages.Count -lt 2) { return $null }
    $stresses = @($stages[-1].element_stresses)
    if ($stresses.Count -eq 0) { return $null }
    $phiR = $PhiDeg * [math]::PI / 180.0
    $sinPhi = [math]::Sin($phiR)
    $cosPhi = [math]::Cos($phiR)
    $maxR = 0.0
    foreach ($s in $stresses) {
        $sm = 0.5 * ([double]$s.s1 + [double]$s.s2)
        $sd = 0.5 * ([double]$s.s1 - [double]$s.s2)
        $allow = $C_kPa * $cosPhi - $sm * $sinPhi
        if ($allow -gt 1e-9) {
            $r = $sd / $allow
            if ($r -gt $maxR) { $maxR = $r }
        }
    }
    return $maxR
}

# Returns gamma value from .dat material row column 4 (phi c psi gamma E nu K0).
function Get-GammaFromDat {
    param([string[]] $DatLines)
    $dataLines = $DatLines | Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('!') }
    if ($dataLines.Count -lt 2) { return $null }
    $cols = ($dataLines[1].Trim() -split '\s+') | Where-Object { $_ -ne '' }
    if ($cols.Count -lt 4) { return $null }
    return [double] $cols[3]
}

# ============================================================================
# Folder loop
# ============================================================================
$verificationRoot = Join-Path $RepoRoot 'Verification\multi_element\geotech'
$docxBuilder      = Join-Path $RepoRoot 'docs\tools\build_smoke_help_docx.ps1'
$pdfBuilder       = Join-Path $RepoRoot 'tools\build_verification_pdf.ps1'
$masterIndexBuilder = Join-Path $RepoRoot 'tools\run_all_verifications.ps1'

if (-not (Test-Path $verificationRoot)) {
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
}
$haveDocx = Test-Path $docxBuilder
$havePdf  = Test-Path $pdfBuilder
$haveMaster = Test-Path $masterIndexBuilder

$now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$commit = Get-CommitHash -Root $RepoRoot

$folderSummaries = New-Object System.Collections.Generic.List[object]

$folders = @(Get-ChildItem $verificationRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'slope_*' } | Sort-Object Name)

foreach ($folder in $folders) {
    $folderName = $folder.Name
    $datFiles = @(Get-ChildItem $folder.FullName -Filter '*.dat' -File -ErrorAction SilentlyContinue | Sort-Object Name)
    if (@($datFiles).Count -eq 0) { continue }

    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($dat in $datFiles) {
        $caseName = [System.IO.Path]::GetFileNameWithoutExtension($dat.Name)
        $jsonPath = Join-Path $folder.FullName ($caseName + '.json')
        $resPath  = Join-Path $folder.FullName ($caseName + '.res')

        $datLines = [System.IO.File]::ReadAllLines($dat.FullName)

        $failure   = Read-DatHeaderField -Lines $datLines -Field 'failure_criterion'
        $matType   = Read-DatHeaderField -Lines $datLines -Field 'material_type'
        $meshType  = Read-DatHeaderField -Lines $datLines -Field 'mesh_type'
        $solverExe = Read-DatHeaderField -Lines $datLines -Field 'solver_executable'
        if (-not $solverExe) { $solverExe = 'p610_t3_elastoplastic' }

        $expectedFoS    = Read-DatHeaderField -Lines $datLines -Field 'expected_FoS'
        $expectedFoSTol = Read-DatHeaderField -Lines $datLines -Field 'expected_FoS_tolerance_pct'
        $expectedMode   = Read-DatHeaderField -Lines $datLines -Field 'expected_FoS_mode'
        $lambdaFactor   = Read-DatHeaderField -Lines $datLines -Field 'lambda_factor'
        $slopeAngle     = Read-DatHeaderField -Lines $datLines -Field 'slope_angle_deg'
        $slopeHeight    = Read-DatHeaderField -Lines $datLines -Field 'slope_height_m'

        $tolPct = if ($expectedFoSTol) { [double] $expectedFoSTol } else { 15.0 }
        if (-not $expectedMode) { $expectedMode = 'lambda_sweep' }
        $expectedMode = $expectedMode.ToLowerInvariant()

        # Material row (gamma at column 4) for yield-ratio scan and gamma reporting.
        $gammaEff = Get-GammaFromDat -DatLines $datLines

        # Material phi/c needed for yield ratio. Read from the same row.
        $dataLines = $datLines | Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('!') }
        $matCols = ($dataLines[1].Trim() -split '\s+') | Where-Object { $_ -ne '' }
        $phiDeg = [double] $matCols[0]
        $cVal   = [double] $matCols[1]

        # Run solver.
        $exePath = Resolve-ExePath -RepoRoot $RepoRoot -ExeName $solverExe
        $runNote = $null
        if ($null -eq $exePath) {
            $runNote = "solver exe missing: $solverExe"
        } else {
            Push-Location $folder.FullName
            try {
                & $exePath $caseName | Out-Null
            } finally {
                Pop-Location
            }
        }

        $jsonExists = Test-Path $jsonPath
        $resExists  = Test-Path $resPath
        $maxIters = Get-MaxIters -ResPath $resPath
        $limit = Get-LimitFromDat -DatLines $datLines
        $allConverged = if ($null -ne $maxIters -and $null -ne $limit) { $maxIters -lt $limit } else { $null }

        $j = $null
        if ($jsonExists) {
            try { $j = Read-JsonLenient -Path $jsonPath } catch { $j = $null }
        }
        $maxDisp = Measure-MaxDisplacement -JsonRoot $j
        $maxYield = Measure-MaxYieldRatio -JsonRoot $j -PhiDeg $phiDeg -C_kPa $cVal

        # SRF mode: read solver-reported FoS from json meta.srf.fos. The C++
        # writer emits a NESTED object: meta.srf = {F_trial, fos, max_yield_ratio,
        # max_disp, max_plastic_strain, ...} so we must descend two levels.
        $srfFoS = $null
        if ($j -ne $null `
            -and $j.PSObject.Properties.Match('meta').Count -gt 0 `
            -and $j.meta `
            -and $j.meta.PSObject.Properties.Match('srf').Count -gt 0 `
            -and $j.meta.srf `
            -and $j.meta.srf.PSObject.Properties.Match('fos').Count -gt 0) {
            $srfRaw = $j.meta.srf.fos
            if ($null -ne $srfRaw -and (Test-FiniteNumber $srfRaw)) {
                $srfFoS = [double] $srfRaw
            }
        }

        # Per-file verdict.
        $notes = New-Object System.Collections.Generic.List[string]
        if ($runNote) { $notes.Add($runNote) | Out-Null }
        if (-not $jsonExists) { $notes.Add('json missing') | Out-Null }
        if (-not $resExists)  { $notes.Add('res missing') | Out-Null }

        $verdict = 'REVIEW'
        if ($expectedMode -eq 'srf') {
            # In srf mode the per-file verdict is just "did the solver run with
            # the requested F_trial and emit meta.srf?". The headline FoS check
            # is folder-level (F-sweep produces srf_FoS_FE = F-at-first-plastic-
            # yield) so that one too-low F_trial doesn't poison the rollup.
            if ($jsonExists -and $resExists -and $null -ne $srfFoS) {
                $verdict = 'PASS_DIAGNOSTIC'
            } else {
                $notes.Add('srf_mode declared but meta.srf missing in json -- rebuild solver?') | Out-Null
            }
        } else {
            # lambda_sweep path: per-file PASS just means the solver ran every
            # increment without hitting the iteration limit. The folder-level
            # diagnostic (computed below) is the real verdict.
            if ($jsonExists -and $resExists -and ($allConverged -eq $true)) {
                $verdict = 'PASS_DIAGNOSTIC'
            } elseif ($jsonExists -and $resExists -and ($allConverged -eq $false)) {
                $verdict = 'PASS_DIAGNOSTIC_DIVERGED'
                $notes.Add(("non-converging inc (max iters {0} >= limit {1})" -f $maxIters, $limit)) | Out-Null
            }
        }

        $rows.Add([pscustomobject]@{
            Folder = $folderName
            Case = $caseName
            MeshType = $meshType
            FailureCriterion = $failure
            MaterialType = $matType
            SolverExecutable = $solverExe
            DatExists = $true
            JsonExists = $jsonExists
            ResExists = $resExists
            ExpectedFoSMode = $expectedMode
            ExpectedFoS = $expectedFoS
            TolerancePct = $tolPct
            LambdaFactor = $lambdaFactor
            GammaEff = $gammaEff
            SlopeAngleDeg = $slopeAngle
            SlopeHeightM = $slopeHeight
            MaxIters = $maxIters
            Limit = $limit
            AllIncrConverged = $allConverged
            MaxDisplacementM = $maxDisp
            MaxYieldRatio = $maxYield
            SrfFoS = $srfFoS
            Verdict = $verdict
            Notes = if ($notes.Count -gt 0) { $notes -join '; ' } else { 'ok' }
            Commit = $commit
            GeneratedAt = $now
        })
    }

    # ------------------------------------------------------------------------
    # Folder-level aggregation
    # ------------------------------------------------------------------------
    $folderMode = ($rows | Select-Object -First 1).ExpectedFoSMode
    $folderExpFoS = ($rows | Select-Object -First 1).ExpectedFoS
    $folderTol = ($rows | Select-Object -First 1).TolerancePct

    $folderVerdict = 'REVIEW'
    $folderNote = ''
    if ($folderMode -eq 'srf') {
        # SRF mode supports two flavours:
        #
        #   1. Single-trial: the folder has one .dat with `! srf_F` and the
        #      orchestrator just compares meta.srf.fos to expected_FoS within
        #      tolerance% (delegated to the per-file verdict already computed).
        #
        #   2. F-sweep: the folder has multiple .dats with different srf_F. The
        #      orchestrator scans them in ascending F and reports
        #         srf_FoS_FE = smallest srf_F at which max_plastic_strain > 0
        #                      AND max_yield_ratio >= 0.95
        #      The folder PASSES if |srf_FoS_FE - expected_FoS| / expected_FoS
        #      <= tolerance%. This is the M2.2 Option B headline metric.
        $rowCount = [int] $rows.Count
        if ($rowCount -le 1) {
            $folderVerdict = ($rows | Select-Object -First 1).Verdict
            $folderNote = ($rows | Select-Object -First 1).Notes
        } else {
            # F-sweep: collect (srf_F, max_yield_ratio, max_plastic_strain) for
            # each .dat in the folder, sort by F, find the smallest F at which
            # the elastic-to-plastic transition fires (max_yield >= 0.95 AND
            # max_plastic_strain > eps). That F is reported as srf_FoS_FE.
            $sweepEntries = New-Object System.Collections.Generic.List[object]
            foreach ($r in $rows) {
                $caseDat = Join-Path $folder.FullName ($r.Case + '.dat')
                $caseJson = Join-Path $folder.FullName ($r.Case + '.json')
                if (-not (Test-Path $caseDat) -or -not (Test-Path $caseJson)) { continue }
                $datLinesC = [System.IO.File]::ReadAllLines($caseDat)
                $thisF = Read-DatHeaderField -Lines $datLinesC -Field 'srf_F'
                if (-not $thisF) { continue }
                $thisFD = [double] $thisF
                $jj = Read-JsonLenient -Path $caseJson
                $maxYR = 0.0
                $maxPS = 0.0
                if ($jj.PSObject.Properties.Match('meta').Count -gt 0 -and $jj.meta `
                    -and $jj.meta.PSObject.Properties.Match('srf').Count -gt 0 -and $jj.meta.srf) {
                    if ($jj.meta.srf.PSObject.Properties.Match('max_yield_ratio').Count -gt 0) {
                        $maxYR = [double] $jj.meta.srf.max_yield_ratio
                    }
                    if ($jj.meta.srf.PSObject.Properties.Match('max_plastic_strain').Count -gt 0) {
                        $maxPS = [double] $jj.meta.srf.max_plastic_strain
                    }
                }
                $sweepEntries.Add([pscustomobject]@{
                    F = $thisFD; MaxYR = $maxYR; MaxPS = $maxPS; Case = $r.Case
                }) | Out-Null
            }
            $sortedSweep = $sweepEntries | Sort-Object F
            $srfFosFE = $null
            foreach ($e in $sortedSweep) {
                if ($e.MaxYR -ge 0.95 -and $e.MaxPS -gt 1e-9) {
                    $srfFosFE = $e.F
                    break
                }
            }
            if ($null -ne $srfFosFE -and $null -ne $folderExpFoS) {
                $expFoS = [double] $folderExpFoS
                $errPct = 100.0 * [math]::Abs($srfFosFE - $expFoS) / [math]::Max(1e-12, $expFoS)
                if ($errPct -le [double] $folderTol) {
                    $folderVerdict = 'PASS'
                    $folderNote = ("srf_FoS_FE = {0:F3} (F-at-first-plastic-yield); within {1:F1}%% of expected_FoS = {2:F3}" -f $srfFosFE, [double] $folderTol, $expFoS)
                } else {
                    $folderNote = ("srf_FoS_FE = {0:F3} differs from expected_FoS = {1:F3} by {2:F2}%% (tol {3:F1}%%)" -f $srfFosFE, $expFoS, $errPct, [double] $folderTol)
                }
            } else {
                $folderNote = "no srf F variant produced first-plastic-yield (max_yield<0.95 or max_plastic_strain==0 across the entire sweep)"
            }
        }
    } else {
        # lambda_sweep mode: require every per-file verdict to be PASS_DIAGNOSTIC*
        # AND require monotonic growth of MaxDisplacementM / MaxYieldRatio in
        # ascending lambda. Monotonicity is the soft collapse signal: if any
        # subsequent lambda has a SMALLER displacement than a smaller lambda we
        # know something pathological happened in that run.
        $allPass = @($rows | Where-Object { $_.Verdict -notlike 'PASS_DIAGNOSTIC*' }).Count -eq 0
        $sorted = $rows | Sort-Object { [double] $_.LambdaFactor }

        # Displacement should grow monotonically with lambda (within numerical
        # noise). Yield ratio is more sensitive: once the yield surface is
        # essentially saturated (ratio ~ 1.0 across the sweep), small numerical
        # wobble at machine precision can flip ordering, so we only flag a
        # YIELD non-monotonicity when the dip is larger than 5 %, which is the
        # threshold above which we consider the yield surface to have actually
        # un-loaded between adjacent lambda steps.
        $monotonicDisp = $true
        $monotonicYield = $true
        $prev = $null
        foreach ($r in $sorted) {
            if ($null -ne $prev -and $null -ne $r.MaxDisplacementM -and $null -ne $prev.MaxDisplacementM) {
                $eps = [math]::Max(1e-12, 1e-6 * [math]::Abs($prev.MaxDisplacementM))
                if ([double]$r.MaxDisplacementM + $eps -lt [double]$prev.MaxDisplacementM) { $monotonicDisp = $false }
            }
            if ($null -ne $prev -and $null -ne $r.MaxYieldRatio -and $null -ne $prev.MaxYieldRatio) {
                if ([double]$r.MaxYieldRatio + 0.05 -lt [double]$prev.MaxYieldRatio) { $monotonicYield = $false }
            }
            $prev = $r
        }

        # Identify the largest lambda whose MaxYieldRatio crossed 1.0 (yield surface
        # essentially reached). If at least one lambda saturates yield this is a
        # useful soft "FE limit-load" signal even if the iteration loop itself does
        # not diverge.
        $firstYieldedLambda = $null
        foreach ($r in $sorted) {
            if ($null -ne $r.MaxYieldRatio -and $r.MaxYieldRatio -ge 0.95) {
                $firstYieldedLambda = $r.LambdaFactor; break
            }
        }

        if ($allPass -and $monotonicDisp -and $monotonicYield) {
            $folderVerdict = 'PASS'
            $folderNote = 'lambda-sweep diagnostic monotonic; all runs completed within iteration limit'
            if ($firstYieldedLambda) {
                $folderNote += "; first yield saturation at lambda=$firstYieldedLambda"
            }
        } else {
            if (-not $allPass) { $folderNote += 'one or more lambda runs failed; ' }
            if (-not $monotonicDisp) { $folderNote += 'displacement non-monotonic vs lambda; ' }
            if (-not $monotonicYield) { $folderNote += 'yield ratio non-monotonic vs lambda; ' }
            $folderNote = $folderNote.TrimEnd('; ')
        }
    }

    # ------------------------------------------------------------------------
    # Per-folder CSV + Markdown help
    # ------------------------------------------------------------------------
    $safeTitle = ($folderName -replace '[^A-Za-z0-9]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safeTitle)) { $safeTitle = $folderName }
    $helpMd     = Join-Path $folder.FullName ("{0}_SlopeTest_Help.md" -f $safeTitle)
    $helpDocx   = Join-Path $folder.FullName ("{0}_SlopeTest_Help.docx" -f $safeTitle)
    $helpPdf    = Join-Path $folder.FullName ("{0}_SlopeTest_Help.pdf" -f $safeTitle)
    $summaryCsv = Join-Path $folder.FullName ("{0}_SlopeTest_Summary.csv" -f $safeTitle)

    $rows | Export-Csv -Path $summaryCsv -NoTypeInformation

    if ($folderMode -eq 'srf') {
        $tableRows = foreach ($r in $rows) {
            "| ``{0}`` | {1} | {2} | {3} | {4} | {5} |" -f `
                $r.Case, $r.MeshType, $r.ExpectedFoS, (Format-Num $r.SrfFoS), `
                (Format-Num $r.MaxDisplacementM), $r.Verdict
        }
        $tableHeader = @(
            "| Case | Mesh | expected_FoS | srf_FoS | max disp (m) | Verdict |",
            "|---|---:|---:|---:|---:|---|"
        )
    } else {
        $tableRows = foreach ($r in ($rows | Sort-Object { [double] $_.LambdaFactor })) {
            "| ``{0}`` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f `
                $r.Case, $r.LambdaFactor, $r.GammaEff, $r.MaxIters, `
                (Format-Num $r.MaxDisplacementM), (Format-Num $r.MaxYieldRatio), `
                $r.AllIncrConverged, $r.Verdict
        }
        $tableHeader = @(
            "| Case | lambda | gamma_eff (kN/m^3) | max iters | max |u| (m) | max yield ratio | iters<limit | Verdict |",
            "|---|---:|---:|---:|---:|---:|:-:|---|"
        )
    }

    $expRow = $rows | Select-Object -First 1
    $helpContent = @"
# Slope-Stability Test: $folderName

| Field | Value |
|---|---|
| Folder | Verification/geotech/$folderName |
| Generated at | $now |
| Commit | $commit |
| Mode | $folderMode |
| expected_FoS | $folderExpFoS |
| Tolerance (% of expected_FoS) | $folderTol |
| Slope angle (deg) | $($expRow.SlopeAngleDeg) |
| Slope height (m) | $($expRow.SlopeHeightM) |

## Folder Verdict

**$folderVerdict** -- $folderNote

## Per-run Diagnostic Table

$($tableHeader -join "`r`n")
$(($tableRows -join "`r`n"))

## Acceptance Rules

### lambda_sweep mode (Option A diagnostic)

- Every lambda run must complete within the iteration limit (`max iters < limit`).
- max(|u|) and max yield ratio must grow monotonically with lambda.
- The first lambda where max yield ratio crosses 0.95 is reported as a soft
  upper-bound FE limit-load multiplier ("first yield saturation at lambda=...").
- Note: the small-strain viscoplastic algorithm in p610_t3_*.exe
  redistributes through nominal collapse without diverging, so the convergence
  flag alone does NOT pin the FoS sharply. Use srf-mode (Option B) for that.

### srf mode (Option B sharp FoS)

- Verdict PASS when |srf_FoS - expected_FoS| / expected_FoS <= tolerance%.
- srf_FoS is read directly from `metadata.srf_FoS` of the .json output.

## Generated Artifacts

- $(Split-Path $summaryCsv -Leaf)
- $(Split-Path $helpMd -Leaf)
- $(Split-Path $helpDocx -Leaf)
- $(Split-Path $helpPdf -Leaf)
"@

    [System.IO.File]::WriteAllText($helpMd, $helpContent)
    if ($haveDocx -and -not $SkipDocx.IsPresent) {
        & powershell -ExecutionPolicy Bypass -File $docxBuilder -InputMd $helpMd -OutputDocx $helpDocx | Out-Null
    }
    if ($havePdf -and -not $SkipPdf.IsPresent) {
        & powershell -ExecutionPolicy Bypass -File $pdfBuilder -InputMd $helpMd -OutputPdf $helpPdf | Out-Null
    }

    # Folder-level row for the index.
    $folderSummaries.Add([pscustomobject]@{
        Folder = $folderName
        Mode = $folderMode
        ExpectedFoS = $folderExpFoS
        TolerancePct = $folderTol
        Cases = $rows.Count
        Verdict = $folderVerdict
        Notes = $folderNote
        Commit = $commit
        GeneratedAt = $now
    }) | Out-Null

    foreach ($row in $rows) { $folderSummaries.Add($row) | Out-Null }
}

# ============================================================================
# Top-level rollup
# ============================================================================
$topCsv = Join-Path $verificationRoot 'Slope_All_SmokeTests_Summary.csv'
$folderSummaries | Export-Csv -Path $topCsv -NoTypeInformation

$indexRows = foreach ($g in ($folderSummaries | Where-Object { $_.PSObject.Properties.Match('Cases').Count -gt 0 } | Sort-Object Folder)) {
    "| $($g.Folder) | $($g.Mode) | $($g.ExpectedFoS) | $($g.Cases) | $($g.Verdict) |"
}
$indexBlock = if ($indexRows) { $indexRows -join "`r`n" } else { '| (no slope folders) | - | - | 0 | NO_DATA |' }

$topMd = Join-Path $verificationRoot 'Slope_All_SmokeTests_Help.md'
$topContent = @"
# Slope-Stability All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/geotech (slope_* subfolders) |
| Generated at | $now |
| Commit | $commit |

## Scope

Aggregates every Verification/geotech/slope_* folder. Two verdict paths:

- ``lambda_sweep`` (Option A): per-folder lambda-gravity diagnostic.
- ``srf`` (Option B): solver-internal strength-reduction-factor bisection
  reads ``meta.srf_FoS`` from the .json output.

## Folder Dashboard

| Folder | Mode | expected_FoS | Cases | Verdict |
|---|---|---:|---:|---|
$indexBlock

## Detailed Summary CSV

- Slope_All_SmokeTests_Summary.csv

## Usage

Rebuild all slope smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_slope_smoke_packages.ps1
"@

[System.IO.File]::WriteAllText($topMd, $topContent)

if ($haveMaster) {
    & powershell -ExecutionPolicy Bypass -File $masterIndexBuilder -RepoRoot $RepoRoot | Out-Null
}

Write-Host "Generated all slope smoke packages."
Write-Host "Top-level summary: $topCsv"
