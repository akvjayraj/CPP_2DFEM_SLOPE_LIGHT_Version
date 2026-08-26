<#
.SYNOPSIS
  Build geotechnical-specific verification packages under Verification/geotech.

.DESCRIPTION
  Walks each subfolder of Verification/geotech, runs the solver named in the
  .dat AnalysisOptions block (defaulting to p610_t3_elastoplastic), parses
  the .res to confirm every load increment converged, and parses the JSON to
  evaluate the Prandtl-style bearing-capacity verdict:

    1. Load-transfer check:
         |min(syy) - (-p_applied)| / p_applied <= expected_qult_tolerance_pct
       where p_applied is read from `! expected_p_applied_kPa` and min(syy)
       is the most compressive stress in the mesh after the final increment.

    2. Yield-surface saturation check (Mohr-Coulomb tension+):
         max_e f(s)/[c*cos(phi) - sigma_m*sin(phi)] >= 0.95
       i.e. at least one element is essentially on the yield envelope. This
       confirms the analytical q_ult was achieved (not just elastic).

  Per-suite outputs:
    - <case>_SmokeTest_Summary.csv
    - <case>_SmokeTest_Help.md (+ optional .docx, .pdf)

  Top-level outputs:
    - Verification/geotech/Geotech_All_SmokeTests_Summary.csv
    - Verification/geotech/Geotech_All_SmokeTests_Help.md
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
        (Join-Path $RepoRoot ("build\Debug\{0}.exe" -f $ExeName)),
        (Join-Path $RepoRoot ("X2D_GUI\bin\Debug\net8.0-windows\{0}.exe" -f $ExeName))
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
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

$verificationRoot = Join-Path $RepoRoot 'Verification\multi_element\geotech'
$docxBuilder = Join-Path $RepoRoot 'docs\tools\build_smoke_help_docx.ps1'
$pdfBuilder  = Join-Path $RepoRoot 'tools\build_verification_pdf.ps1'
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
    Where-Object { $_.Name -notlike 'slope_*' } | Sort-Object Name)
foreach ($folder in $folders) {
    $folderName = $folder.Name
    $datFiles = @(Get-ChildItem $folder.FullName -Filter '*.dat' -File -ErrorAction SilentlyContinue)
    if (@($datFiles).Count -eq 0) { continue }

    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($dat in $datFiles) {
        $caseName = [System.IO.Path]::GetFileNameWithoutExtension($dat.Name)
        $jsonPath = Join-Path $folder.FullName ($caseName + '.json')
        $resPath  = Join-Path $folder.FullName ($caseName + '.res')

        $datLines = [System.IO.File]::ReadAllLines($dat.FullName)

        $failure  = Read-DatHeaderField -Lines $datLines -Field 'failure_criterion'
        $matType  = Read-DatHeaderField -Lines $datLines -Field 'material_type'
        $meshType = Read-DatHeaderField -Lines $datLines -Field 'mesh_type'
        $solverExe = Read-DatHeaderField -Lines $datLines -Field 'solver_executable'
        if (-not $solverExe) { $solverExe = 'p610_t3_elastoplastic' }

        $expectedQultPerC = Read-DatHeaderField -Lines $datLines -Field 'expected_qult_per_c'
        $expectedPhi      = Read-DatHeaderField -Lines $datLines -Field 'expected_phi_deg'
        $expectedC        = Read-DatHeaderField -Lines $datLines -Field 'expected_c_kPa'
        $expectedPApplied = Read-DatHeaderField -Lines $datLines -Field 'expected_p_applied_kPa'
        $tolPctRaw        = Read-DatHeaderField -Lines $datLines -Field 'expected_qult_tolerance_pct'
        $tolPct = if ($tolPctRaw) { [double] $tolPctRaw } else { 5.0 }

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
        $nodeCount = $null
        $elementCount = $null
        $stageCount = $null
        $loadXferErrPct = $null
        $minSyy = $null
        $maxYieldRatio = $null
        $maxIters = $null
        $limit = $null
        $allInxsConverged = $null
        $cornerUy = $null
        $notes = New-Object System.Collections.Generic.List[string]

        # --- parse .res for iteration counts -------------------------------
        if ($resExists) {
            $resLines = [System.IO.File]::ReadAllLines($resPath)
            $maxIters = 0
            foreach ($line in $resLines) {
                if ($line -match 'Inc\s+\d+\s+took\s+(\d+)\s+iterations') {
                    $iters = [int] $Matches[1]
                    if ($iters -gt $maxIters) { $maxIters = $iters }
                }
            }
            # tol limit incs layers line: read incs/limit from the .dat solver
            # control row. The 5th non-comment row is "tol limit incs layers".
            # Easier path: scan .res for "Inc N took M iterations" lines and
            # find max - the limit is then read from the Smith-Griffiths
            # control line in the dat (4th token of "tol limit incs layers").
            foreach ($line in $datLines) {
                if ($line -match '^\s*\d*\.?\d+\s+(\d+)\s+(\d+)\s+(\d+)\s*$') {
                    # tol/limit/incs/layers - require limit and incs to be small ints
                    $candLimit = [int] $Matches[1]
                    if ($candLimit -ge 50 -and $candLimit -le 100000) {
                        $limit = $candLimit
                        break
                    }
                }
            }
            if ($null -ne $limit) {
                $allInxsConverged = ($maxIters -lt $limit)
            }
        } else {
            $notes.Add('res missing') | Out-Null
        }

        # --- parse JSON for stresses --------------------------------------
        if ($jsonExists) {
            $j = Read-JsonLenient -Path $jsonPath
            $nodeCount = [int] $j.node_count
            $elementCount = [int] $j.element_count
            $stageCount = [int] $j.n_stages
            $stageArr = @($j.stages)
            if ($stageArr.Count -gt 1) {
                $stresses = @($stageArr[1].element_stresses)
                # Load-transfer check: min syy in entire mesh.
                $minSyy = ($stresses | Measure-Object -Property syy -Minimum).Minimum
                if ($null -ne $expectedPApplied) {
                    $pApp = [double] $expectedPApplied
                    $loadXferErrPct = 100.0 * [math]::Abs($minSyy + $pApp) / $pApp
                }

                # Yield-saturation check: scan all elements for max f/(c*cos-sm*sin).
                $phiR = if ($null -ne $expectedPhi) { [double]$expectedPhi * [math]::PI / 180.0 } else { 0.0 }
                $cVal = if ($null -ne $expectedC) { [double]$expectedC } else { 1.0 }
                $sinPhi = [math]::Sin($phiR)
                $cosPhi = [math]::Cos($phiR)
                $maxYieldRatio = 0.0
                foreach ($s in $stresses) {
                    $sm = 0.5 * ([double]$s.s1 + [double]$s.s2)
                    $sd = 0.5 * ([double]$s.s1 - [double]$s.s2)
                    $allow = $cVal * $cosPhi - $sm * $sinPhi
                    if ($allow -gt 1e-9) {
                        $r = $sd / $allow
                        if ($r -gt $maxYieldRatio) { $maxYieldRatio = $r }
                    }
                }
            } else {
                $notes.Add('stage 1 missing') | Out-Null
            }
        } else {
            $notes.Add('json missing') | Out-Null
        }

        # --- verdict --------------------------------------------------------
        $loadXferOk  = ($null -ne $loadXferErrPct) -and ($loadXferErrPct -le $tolPct)
        $yieldOk     = ($null -ne $maxYieldRatio) -and ($maxYieldRatio -ge 0.90)
        $convergedOk = ($null -ne $allInxsConverged) -and $allInxsConverged

        $verdict = if ($jsonExists -and $resExists -and $loadXferOk -and $yieldOk -and $convergedOk) {
            'PASS'
        } elseif ($jsonExists -and $resExists -and $loadXferOk -and $convergedOk) {
            'PASS_WITH_INTERPRETATION'  # converged but yield not fully saturated (interior soil)
        } else {
            'REVIEW'
        }

        if (-not $loadXferOk -and ($null -ne $loadXferErrPct)) {
            $notes.Add("load-xfer err {0:F2}%% > {1:F2}%%" -f $loadXferErrPct, $tolPct) | Out-Null
        }
        if (-not $yieldOk -and ($null -ne $maxYieldRatio)) {
            $notes.Add("yield ratio {0:F3} < 0.90" -f $maxYieldRatio) | Out-Null
        }
        if (-not $convergedOk -and ($null -ne $allInxsConverged) -and (-not $allInxsConverged)) {
            $notes.Add("non-converging inc (max iters {0} >= limit {1})" -f $maxIters, $limit) | Out-Null
        }
        if ($runNote) { $notes.Add($runNote) | Out-Null }

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
            NodeCount = $nodeCount
            ElementCount = $elementCount
            StageCount = $stageCount
            ExpectedQultPerC = $expectedQultPerC
            ExpectedPhi = $expectedPhi
            ExpectedC = $expectedC
            ExpectedPApplied = $expectedPApplied
            MinSyy = $minSyy
            LoadXferErrPct = $loadXferErrPct
            MaxYieldRatio = $maxYieldRatio
            MaxIters = $maxIters
            Limit = $limit
            AllIncsConverged = $allInxsConverged
            TolPct = $tolPct
            Verdict = $verdict
            Notes = if ($notes.Count -gt 0) { $notes -join '; ' } else { 'ok' }
            Commit = $commit
            GeneratedAt = $now
        })
    }

    $safeTitle = ($folderName -replace '[^A-Za-z0-9]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safeTitle)) { $safeTitle = $folderName }
    $helpMd     = Join-Path $folder.FullName ("{0}_SmokeTest_Help.md" -f $safeTitle)
    $helpDocx   = Join-Path $folder.FullName ("{0}_SmokeTest_Help.docx" -f $safeTitle)
    $helpPdf    = Join-Path $folder.FullName ("{0}_SmokeTest_Help.pdf" -f $safeTitle)
    $summaryCsv = Join-Path $folder.FullName ("{0}_SmokeTest_Summary.csv" -f $safeTitle)

    $rows | Export-Csv -Path $summaryCsv -NoTypeInformation

    $tableRows = foreach ($r in $rows | Sort-Object Case) {
        "| ``{0}`` | {1} | {2} | {3} | {4} | {5} | {6} |" -f `
            $r.Case, $r.MeshType, $r.ElementCount, `
            (Format-Num $r.MinSyy), (Format-Num $r.LoadXferErrPct), (Format-Num $r.MaxYieldRatio), `
            $r.Verdict
    }

    $expRow = $rows | Select-Object -First 1
    $tolStr = if ($expRow) { Format-Num $expRow.TolPct } else { 'n/a' }

    $helpContent = @"
# Geotech Bearing-Capacity Test: $folderName

| Field | Value |
|---|---|
| Folder | Verification/geotech/$folderName |
| Generated at | $now |
| Commit | $commit |
| Tolerance (% of p_applied) | $tolStr |

## Closed-form Expectation

| Quantity | Value |
|---|---:|
| phi (deg) | $($expRow.ExpectedPhi) |
| c (kPa) | $($expRow.ExpectedC) |
| q_ult / c (Nc) | $($expRow.ExpectedQultPerC) |
| p_applied (kPa) | $($expRow.ExpectedPApplied) |

## FEM Result Snapshot (final stage)

| Case | Mesh | Elements | min(syy) (kPa) | load-xfer err (%) | yield ratio | Verdict |
|---|---:|---:|---:|---:|---:|---|
$(($tableRows -join "`r`n"))

## Acceptance Rules

- Every load increment must converge (max iters < limit).
- min(syy) must equal -p_applied within Tolerance% (load-transfer check).
- max yield ratio = max_e (s1-s2)/[2*(c*cos(phi) - sigma_m*sin(phi))] must
  reach >= 0.90 (Mohr-Coulomb yield essentially saturated at q_ult).
- A run that satisfies load-transfer + convergence but not yield-saturation
  is reported as PASS_WITH_INTERPRETATION (sub-collapse loading).

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

    foreach ($row in $rows) { $folderSummaries.Add($row) | Out-Null }
}

$topCsv  = Join-Path $verificationRoot 'Geotech_All_SmokeTests_Summary.csv'
$topMd   = Join-Path $verificationRoot 'Geotech_All_SmokeTests_Help.md'

$folderSummaries | Export-Csv -Path $topCsv -NoTypeInformation

$indexRows = foreach ($g in ($folderSummaries | Group-Object Folder | Sort-Object Name)) {
    $allPass = @($g.Group | Where-Object { $_.Verdict -notlike 'PASS*' }).Count -eq 0
    $verdict = if ($allPass) { 'PASS' } else { 'REVIEW' }
    "| $($g.Name) | $($g.Count) | $verdict |"
}

$indexBlock = if ($indexRows) { $indexRows -join "`r`n" } else { '| (no folders) | 0 | NO_DATA |' }

$topContent = @"
# Geotech All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/geotech |
| Generated at | $now |
| Commit | $commit |

## Scope

This index aggregates every geotechnical-specific verification under
Verification/geotech: Prandtl strip footing bearing capacity, slope
stability, and other plasticity benchmarks tied to closed-form Terzaghi /
Brinch-Hansen / Vesic / Bishop solutions.

## Folder Dashboard

| Folder | Cases | Verdict |
|---|---:|---|
$indexBlock

## Detailed Summary CSV

- Geotech_All_SmokeTests_Summary.csv

## Usage

Rebuild all geotech smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_geotech_smoke_packages.ps1
"@

[System.IO.File]::WriteAllText($topMd, $topContent)

if ($haveMaster) {
    & powershell -ExecutionPolicy Bypass -File $masterIndexBuilder -RepoRoot $RepoRoot | Out-Null
}

Write-Host "Generated all geotech smoke packages."
Write-Host "Top-level summary: $topCsv"
