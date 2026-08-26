<#
.SYNOPSIS
  Build NAFEMS-style verification packages under Verification/nafems.

.DESCRIPTION
  Walks each subfolder of Verification/nafems, runs the solver named in the
  .dat AnalysisOptions block (defaulting to p610_t3_elastoplastic), parses the
  per-element stresses from the JSON output, and compares against the
  closed-form expectations encoded in the .dat header as
    ! expected_sxx_kPa <value>
    ! expected_syy_kPa <value>
    ! expected_txy_kPa <value>
    ! expected_szz_kPa <value>
    ! tolerance_kPa   <value>
  All elements in stage 1 must satisfy the closed-form within tolerance.

  Per-suite outputs:
    - <case>_SmokeTest_Summary.csv
    - <case>_SmokeTest_Help.md (+ optional .docx, .pdf)

  Top-level outputs:
    - Verification/nafems/NAFEMS_All_SmokeTests_Summary.csv
    - Verification/nafems/NAFEMS_All_SmokeTests_Help.md (+ optional .docx, .pdf)

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/build_all_nafems_smoke_packages.ps1
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
        (Join-Path $RepoRoot ("Verification\{0}.exe" -f $ExeName)),
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

# After the multi/single-element reorganization the NAFEMS suite is split
# across two category roots:
#   - Verification\multi_element\nafems_le10_*, nafems_le11_*, nafems_le1_*
#   - Verification\single_element\nafems_patch_*, nafems_pureshear_*
# Walk both. The aggregate help.md and CSV go to multi_element\nafems_index
# (which is where the previous Verification\nafems index files were moved).
$verificationRoot = Join-Path $RepoRoot 'Verification'
$nafemsScanRoots = @(
    (Join-Path $verificationRoot 'multi_element'),
    (Join-Path $verificationRoot 'single_element')
)
$nafemsAggregateRoot = Join-Path $verificationRoot 'multi_element\nafems_index'
$docxBuilder = Join-Path $RepoRoot 'docs\tools\build_smoke_help_docx.ps1'
$pdfBuilder  = Join-Path $RepoRoot 'tools\build_verification_pdf.ps1'
$masterIndexBuilder = Join-Path $RepoRoot 'tools\run_all_verifications.ps1'

if (-not (Test-Path $nafemsAggregateRoot)) {
    New-Item -ItemType Directory -Path $nafemsAggregateRoot -Force | Out-Null
}
if (-not (Test-Path $docxBuilder))        { throw "DOCX builder not found: $docxBuilder" }
if (-not (Test-Path $pdfBuilder))         { throw "PDF builder not found: $pdfBuilder" }
if (-not (Test-Path $masterIndexBuilder)) { throw "Master index builder not found: $masterIndexBuilder" }

$now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$commit = Get-CommitHash -Root $RepoRoot

$folderSummaries = New-Object System.Collections.Generic.List[object]

$folders = @(
    foreach ($root in $nafemsScanRoots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'nafems_*' }
    }
) | Sort-Object Name
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

        $expectedSxx = Read-DatHeaderField -Lines $datLines -Field 'expected_sxx_kPa'
        $expectedSyy = Read-DatHeaderField -Lines $datLines -Field 'expected_syy_kPa'
        $expectedTxy = Read-DatHeaderField -Lines $datLines -Field 'expected_txy_kPa'
        $expectedSzz = Read-DatHeaderField -Lines $datLines -Field 'expected_szz_kPa'
        $toleranceVal = Read-DatHeaderField -Lines $datLines -Field 'tolerance_kPa'

        $tolerance = if ($toleranceVal) { [double] $toleranceVal } else { 1e-3 }

        # Optional NAFEMS LE10 / LE11-style Lame radial-field expectation.
        # When present, all elements in stage 1 are compared to the closed-form
        # sigma_rr / sigma_tt at their centroid (rotated into Cartesian) within
        # the per-test tolerance. See Verification/nafems/nafems_le10_*/.dat for
        # the full header schema.
        $expRadialMode    = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_mode'
        $expRadialA       = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_inner_a'
        $expRadialB       = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_outer_b'
        $expRadialP       = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_pressure_kPa'
        $expRadialNu      = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_nu'
        $expRadialCx      = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_center_x'
        $expRadialCy      = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_center_y'
        $expRadialPlaneStrainTok = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_plane_strain'
        $expRadialTolKpa  = Read-DatHeaderField -Lines $datLines -Field 'expected_radial_tolerance_kPa'
        $hasRadialField = ($null -ne $expRadialMode) -and ($null -ne $expRadialA) -and `
                          ($null -ne $expRadialB)    -and ($null -ne $expRadialP) -and `
                          ($null -ne $expRadialNu)
        if ($hasRadialField -and $expRadialTolKpa) {
            $tolerance = [double] $expRadialTolKpa
        }

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
        $maxAbsSxxErr = $null
        $maxAbsSyyErr = $null
        $maxAbsTxyErr = $null
        $maxAbsSzzErr = $null
        $worstStresses = $null
        $notes = New-Object System.Collections.Generic.List[string]

        $maxAbsLameErr = $null
        if ($jsonExists) {
            $j = Read-JsonLenient -Path $jsonPath
            $nodeCount = [int] $j.node_count
            $elementCount = [int] $j.element_count
            $stageCount = [int] $j.n_stages
            $stageArr = @($j.stages)
            if ($stageArr.Count -gt 1) {
                $stresses = @($stageArr[1].element_stresses)
                $worstStresses = $stresses[0]

                $maxAbsSxxErr = 0.0
                $maxAbsSyyErr = 0.0
                $maxAbsTxyErr = 0.0
                $maxAbsSzzErr = 0.0

                foreach ($s in $stresses) {
                    if ($null -ne $expectedSxx) {
                        $err = [math]::Abs([double]$s.sxx - [double]$expectedSxx)
                        if ($err -gt $maxAbsSxxErr) { $maxAbsSxxErr = $err; $worstStresses = $s }
                    }
                    if ($null -ne $expectedSyy) {
                        $err = [math]::Abs([double]$s.syy - [double]$expectedSyy)
                        if ($err -gt $maxAbsSyyErr) { $maxAbsSyyErr = $err }
                    }
                    if ($null -ne $expectedTxy) {
                        $err = [math]::Abs([double]$s.txy - [double]$expectedTxy)
                        if ($err -gt $maxAbsTxyErr) { $maxAbsTxyErr = $err }
                    }
                    if ($null -ne $expectedSzz) {
                        $err = [math]::Abs([double]$s.szz - [double]$expectedSzz)
                        if ($err -gt $maxAbsSzzErr) { $maxAbsSzzErr = $err }
                    }
                }

                # Lame radial-field check: per-element centroid -> closed-form
                # sigma_rr/sigma_tt -> Cartesian -> max abs error over (sxx, syy,
                # txy [, szz when plane-strain]). Single-pass; reuses element
                # connectivity from the JSON (1-based n1..nN keys) and node x/y.
                if ($hasRadialField -and $stresses.Count -gt 0) {
                    $a   = [double] $expRadialA
                    $b   = [double] $expRadialB
                    $p   = [double] $expRadialP
                    $nu  = [double] $expRadialNu
                    $cx  = if ($null -ne $expRadialCx) { [double] $expRadialCx } else { 0.0 }
                    $cy  = if ($null -ne $expRadialCy) { [double] $expRadialCy } else { 0.0 }
                    $planeStrain = $true
                    if ($null -ne $expRadialPlaneStrainTok) {
                        $planeStrain = ($expRadialPlaneStrainTok -match '(?i)^(true|1|yes)$')
                    }
                    $factor = $p * $a * $a / ($b * $b - $a * $a)

                    $nodeById = @{}
                    foreach ($nd in @($j.nodes)) {
                        $nodeById[[int]$nd.id] = @{ X = [double]$nd.x; Y = [double]$nd.y }
                    }
                    $elemById = @{}
                    foreach ($el in @($j.elements)) {
                        $ids = New-Object System.Collections.Generic.List[int]
                        foreach ($prop in $el.PSObject.Properties) {
                            if ($prop.Name -match '^n\d+$' -and $null -ne $prop.Value) {
                                $ids.Add([int] $prop.Value)
                            }
                        }
                        $elemById[[int]$el.id] = $ids.ToArray()
                    }

                    $maxAbsLameErr = 0.0
                    foreach ($s in $stresses) {
                        $eid = [int] $s.id
                        if (-not $elemById.ContainsKey($eid)) { continue }
                        $sumX = 0.0; $sumY = 0.0; $cnt = 0
                        foreach ($nid in $elemById[$eid]) {
                            if (-not $nodeById.ContainsKey($nid)) { continue }
                            $sumX += $nodeById[$nid].X
                            $sumY += $nodeById[$nid].Y
                            $cnt++
                        }
                        if ($cnt -eq 0) { continue }
                        $xc = ($sumX / $cnt) - $cx
                        $yc = ($sumY / $cnt) - $cy
                        $r  = [math]::Sqrt($xc * $xc + $yc * $yc)
                        if ($r -le 0) { continue }
                        $th = [math]::Atan2($yc, $xc)
                        $srr = $factor * (1.0 - $b * $b / ($r * $r))
                        $stt = $factor * (1.0 + $b * $b / ($r * $r))
                        $cTh = [math]::Cos($th); $sTh = [math]::Sin($th)
                        $sxxA = $srr * $cTh * $cTh + $stt * $sTh * $sTh
                        $syyA = $srr * $sTh * $sTh + $stt * $cTh * $cTh
                        $txyA = ($srr - $stt) * $sTh * $cTh
                        $err = [math]::Abs([double]$s.sxx - $sxxA)
                        if ($err -gt $maxAbsLameErr) { $maxAbsLameErr = $err; $worstStresses = $s }
                        $err = [math]::Abs([double]$s.syy - $syyA)
                        if ($err -gt $maxAbsLameErr) { $maxAbsLameErr = $err }
                        $err = [math]::Abs([double]$s.txy - $txyA)
                        if ($err -gt $maxAbsLameErr) { $maxAbsLameErr = $err }
                        if ($planeStrain) {
                            $szzA = $nu * ($srr + $stt)
                            $err = [math]::Abs([double]$s.szz - $szzA)
                            if ($err -gt $maxAbsLameErr) { $maxAbsLameErr = $err }
                        }
                    }
                }
            } else {
                $notes.Add('stage 1 missing') | Out-Null
            }
        } else {
            $notes.Add('json missing') | Out-Null
        }

        if (-not $resExists) { $notes.Add('res missing') | Out-Null }
        if ($runNote)        { $notes.Add($runNote) | Out-Null }

        $stressOk = $true
        if ($null -ne $maxAbsSxxErr -and $maxAbsSxxErr -gt $tolerance) { $stressOk = $false }
        if ($null -ne $maxAbsSyyErr -and $maxAbsSyyErr -gt $tolerance) { $stressOk = $false }
        if ($null -ne $maxAbsTxyErr -and $maxAbsTxyErr -gt $tolerance) { $stressOk = $false }
        if ($null -ne $maxAbsSzzErr -and $maxAbsSzzErr -gt $tolerance) { $stressOk = $false }
        if ($null -ne $maxAbsLameErr -and $maxAbsLameErr -gt $tolerance) { $stressOk = $false }

        if (-not $stressOk) { $notes.Add('closed-form mismatch') | Out-Null }

        $verdict = if ($jsonExists -and $resExists -and $stressOk -and $stageCount -ge 2) { 'PASS' } else { 'REVIEW' }

        $sxxOut = if ($worstStresses) { [double]$worstStresses.sxx } else { $null }
        $syyOut = if ($worstStresses) { [double]$worstStresses.syy } else { $null }
        $txyOut = if ($worstStresses) { [double]$worstStresses.txy } else { $null }
        $szzOut = if ($worstStresses) { [double]$worstStresses.szz } else { $null }

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
            ExpectedSxx = $expectedSxx
            ExpectedSyy = $expectedSyy
            ExpectedTxy = $expectedTxy
            ExpectedSzz = $expectedSzz
            Tolerance = $tolerance
            MaxAbsSxxErr = $maxAbsSxxErr
            MaxAbsSyyErr = $maxAbsSyyErr
            MaxAbsTxyErr = $maxAbsTxyErr
            MaxAbsSzzErr = $maxAbsSzzErr
            MaxAbsLameErr = $maxAbsLameErr
            WorstSxx = $sxxOut
            WorstSyy = $syyOut
            WorstTxy = $txyOut
            WorstSzz = $szzOut
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
        "| ``{0}`` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f `
            $r.Case, $r.MeshType, $r.ElementCount, `
            (Format-Num $r.WorstSxx), (Format-Num $r.WorstSyy), (Format-Num $r.WorstTxy), (Format-Num $r.WorstSzz), `
            $r.Verdict
    }

    $expRow = $rows | Select-Object -First 1
    $tolStr = if ($expRow) { Format-Num $expRow.Tolerance } else { 'n/a' }

    $helpContent = @"
# NAFEMS-style Patch Test: $folderName

| Field | Value |
|---|---|
| Folder | Verification/nafems/$folderName |
| Generated at | $now |
| Commit | $commit |
| Tolerance (kPa) | $tolStr |

## Closed-form Expectation

| Quantity | Expected (kPa) |
|---|---:|
| sxx | $($expRow.ExpectedSxx) |
| syy | $($expRow.ExpectedSyy) |
| txy | $($expRow.ExpectedTxy) |
| szz | $($expRow.ExpectedSzz) |

## FEM Result Snapshot (worst element, Stage 1)

| Case | Mesh | Elements | sxx (kPa) | syy (kPa) | txy (kPa) | szz (kPa) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
$(($tableRows -join "`r`n"))

## Acceptance Rules

- All elements must satisfy the closed-form within Tolerance (kPa).
- node_count, element_count, n_stages = 2 must be present in the solver JSON.
- Stage 1 element_stresses array must be finite for every element.
- For plane-strain elasticity szz = nu*(sxx + syy); the closed-form value above
  was pre-computed for the linear-elastic regime (the test pins the material to
  pseudo-elastic by raising cohesion / friction angle so plasticity never
  activates).

## Generated Artifacts

- $(Split-Path $summaryCsv -Leaf)
- $(Split-Path $helpMd -Leaf)
- $(Split-Path $helpDocx -Leaf)
- $(Split-Path $helpPdf -Leaf)
"@

    [System.IO.File]::WriteAllText($helpMd, $helpContent)
    if (-not $SkipDocx.IsPresent) {
        & powershell -ExecutionPolicy Bypass -File $docxBuilder -InputMd $helpMd -OutputDocx $helpDocx | Out-Null
    }
    if (-not $SkipPdf.IsPresent) {
        & powershell -ExecutionPolicy Bypass -File $pdfBuilder -InputMd $helpMd -OutputPdf $helpPdf | Out-Null
    }

    foreach ($row in $rows) { $folderSummaries.Add($row) | Out-Null }
}

$topCsv  = Join-Path $nafemsAggregateRoot 'NAFEMS_All_SmokeTests_Summary.csv'
$topMd   = Join-Path $nafemsAggregateRoot 'NAFEMS_All_SmokeTests_Help.md'
$topDocx = Join-Path $nafemsAggregateRoot 'NAFEMS_All_SmokeTests_Help.docx'
$topPdf  = Join-Path $nafemsAggregateRoot 'NAFEMS_All_SmokeTests_Help.pdf'

$folderSummaries | Export-Csv -Path $topCsv -NoTypeInformation

$indexRows = foreach ($g in ($folderSummaries | Group-Object Folder | Sort-Object Name)) {
    $allPass = @($g.Group | Where-Object { $_.Verdict -notlike 'PASS*' }).Count -eq 0
    $verdict = if ($allPass) { 'PASS' } else { 'REVIEW' }
    "| $($g.Name) | $($g.Count) | $verdict |"
}

$indexBlock = if ($indexRows) { $indexRows -join "`r`n" } else { '| (no folders) | 0 | NO_DATA |' }

$topContent = @"
# NAFEMS All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/nafems |
| Generated at | $now |
| Commit | $commit |

## Scope

This index aggregates every NAFEMS-style patch test under Verification/nafems and
provides a professional regression dashboard. Each subfolder contains one or
more closed-form-checked patch tests where the FEM result is compared against
an analytical reference within a per-test tolerance.

## Folder Dashboard

| Folder | Cases | Verdict |
|---|---:|---|
$indexBlock

## Detailed Summary CSV

- NAFEMS_All_SmokeTests_Summary.csv

## Usage

Rebuild all NAFEMS smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_nafems_smoke_packages.ps1
"@

[System.IO.File]::WriteAllText($topMd, $topContent)

if (-not $SkipDocx.IsPresent) {
    & powershell -ExecutionPolicy Bypass -File $docxBuilder -InputMd $topMd -OutputDocx $topDocx | Out-Null
}
if (-not $SkipPdf.IsPresent) {
    & powershell -ExecutionPolicy Bypass -File $pdfBuilder -InputMd $topMd -OutputPdf $topPdf | Out-Null
}

& powershell -ExecutionPolicy Bypass -File $masterIndexBuilder -RepoRoot $RepoRoot | Out-Null

Write-Host "Generated all NAFEMS smoke packages."
Write-Host "Top-level summary: $topCsv"
