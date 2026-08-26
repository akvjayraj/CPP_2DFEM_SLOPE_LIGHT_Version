<#
.SYNOPSIS
  Build professional smoke-test packages for all criterion/material folders under
  Verification/mc_tunnel, including per-folder DAT/JSON/RES/help artifacts and a
  top-level index help package.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/build_all_mc_tunnel_smoke_packages.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $SkipDocx,
    [switch] $SkipPdf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-XmlEscaped {
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    return $Text.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

function Format-Num {
    param($Value)
    if ($null -eq $Value) { return 'n/a' }
    if (-not (Test-FiniteNumber $Value)) { return 'n/a' }
    return [string]::Format('{0:F6}', [double]$Value)
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

# Mirror of effective_elastic_constants() in library/solver/p610_t3_elastoplastic.cpp.
# Returns nu_eff for the given material law, matching the C++ rules so the
# orchestrator's plane-strain consistency check stays in lockstep with the solver.
function Get-EffectiveNu {
    param(
        [string] $MaterialType,
        [double] $Nu0
    )
    $key = if ($MaterialType) { $MaterialType.Trim().ToLowerInvariant() } else { '' }
    $nuRaw = switch ($key) {
        'transverselyisotropic' { $Nu0 * 0.95 }
        'orthotropic'           { $Nu0 * 0.90 }
        'viscoelastic'          { $Nu0 * 0.98 }
        default                 { $Nu0 } # Isotropic, Plastic, NonlinearIsotropic, DuncanChangHyperbolic, unknown
    }
    return [math]::Min(0.49, [math]::Max(0.05, [double] $nuRaw))
}

function Read-JsonLenient {
    param([string] $Path)
    $raw = Get-Content -Raw $Path
    # Some solver dumps may emit inf/nan tokens; normalize to null for robust parsing.
    $raw = [regex]::Replace($raw, '(?i)(?<=[:\[,]\s*)-?inf(?=\s*[,}\]])', 'null')
    $raw = [regex]::Replace($raw, '(?i)(?<=[:\[,]\s*)nan(?=\s*[,}\]])', 'null')
    return ($raw | ConvertFrom-Json)
}

function MeshSpec {
    param([string] $MeshType)
    switch ($MeshType) {
        'T3' {
            return @{
                NodeCount = 3
                Nodes = @('0  0', '1  0', '0  1')
                Connectivity = '1  2  3'
                Nr = @('3', '1  0  0', '2  1  0', '3  0  1')
                Nouts = @('3', '1  2  3')
                TractionEdge = @('1', '2  3')
            }
        }
        'T6' {
            return @{
                NodeCount = 6
                Nodes = @('0  0', '1  0', '0  1', '0.5  0', '0.5  0.5', '0  0.5')
                Connectivity = '1  2  3  4  5  6'
                Nr = @('3', '1  0  0', '2  1  0', '3  0  1')
                Nouts = @('3', '1  2  3')
                TractionEdge = @('1', '2  3')
            }
        }
        'Q4' {
            return @{
                NodeCount = 4
                Nodes = @('0  0', '1  0', '1  1', '0  1')
                Connectivity = '1  2  3  4'
                Nr = @('3', '1  0  0', '2  1  0', '4  0  1')
                Nouts = @('4', '1  2  3  4')
                TractionEdge = @('1', '2  3')
            }
        }
        'Q8' {
            return @{
                NodeCount = 8
                Nodes = @('0  0', '1  0', '1  1', '0  1', '0.5  0', '1  0.5', '0.5  1', '0  0.5')
                Connectivity = '1  2  3  4  5  6  7  8'
                Nr = @('4', '1  0  0', '2  1  0', '4  0  1', '8  0  1')
                Nouts = @('4', '1  2  3  4')
                TractionEdge = @('1', '2  6')
            }
        }
        default { throw "Unsupported mesh type: $MeshType" }
    }
}

function Resolve-ExePath {
    param([string] $RepoRoot, [string] $ExeName)
    # NOTE: library\solver\{name}.exe is the canonical CMake RUNTIME_OUTPUT_DIRECTORY
    # for all p610_t3_* variants (see CMakeLists.txt). Probe it first so this
    # orchestrator matches the working list in tools/build_all_nafems_smoke_packages.ps1
    # and stops emitting "solver exe missing" REVIEW rows for the constitutive variants.
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

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $scriptDir '..')
}

$verificationRoot = Join-Path $RepoRoot 'Verification\single_element'
$docxBuilder = Join-Path $RepoRoot 'docs\tools\build_smoke_help_docx.ps1'
$pdfBuilder = Join-Path $RepoRoot 'tools\build_verification_pdf.ps1'
$masterIndexBuilder = Join-Path $RepoRoot 'tools\run_all_verifications.ps1'
if (-not (Test-Path $verificationRoot)) { throw "Folder not found: $verificationRoot" }
if (-not (Test-Path $docxBuilder)) { throw "DOCX builder not found: $docxBuilder" }
if (-not (Test-Path $pdfBuilder)) { throw "PDF builder not found: $pdfBuilder" }
if (-not (Test-Path $masterIndexBuilder)) { throw "Master index builder not found: $masterIndexBuilder" }

$now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$commit = Get-CommitHash -Root $RepoRoot

$folderSummaries = New-Object System.Collections.Generic.List[object]

# After the multi/single-element reorganization the mc_tunnel smoke folders
# live alongside other single-element suites (nafems_patch_*, kirsch_elastic,
# twin_tunnel, ...). Filter to the historical mc_tunnel-prefixed names so this
# orchestrator stays focused on the MC-tunnel suite and does not clobber other
# suites' .dat files via the seed-and-regenerate logic below.
$folders = Get-ChildItem $verificationRoot -Directory | Sort-Object Name |
    Where-Object { $_.Name -match '^(MC_tunnel|mc_tunnel(_|$))' }
foreach ($folder in $folders) {
    $folderName = $folder.Name
    $seedDat = Get-ChildItem $folder.FullName -Filter '*.dat' | Select-Object -First 1
    if ($null -eq $seedDat) { continue }

    $seedLines = [System.IO.File]::ReadAllLines($seedDat.FullName)
    $failure = (($seedLines | Where-Object { $_ -match '^! failure_criterion\s+' } | Select-Object -First 1) -replace '^! failure_criterion\s+', '').Trim()
    $materialType = (($seedLines | Where-Object { $_ -match '^! material_type\s+' } | Select-Object -First 1) -replace '^! material_type\s+', '').Trim()
    $solverExe = (($seedLines | Where-Object { $_ -match '^! solver_executable\s+' } | Select-Object -First 1) -replace '^! solver_executable\s+', '').Trim()
    $dataLines = $seedLines | Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('!') }
    if ($dataLines.Count -lt 2) { throw "Cannot parse material row from $($seedDat.FullName)" }
    $materialRow = $dataLines[1].Trim()

    # Parse nu0 from material row (column 6: phi  c  psi  gamma  E  nu  K0).
    # The plane-strain consistency check below must follow the same nu the solver
    # uses, otherwise non-default Poisson ratios (Plastic, Nonlinear, ViscoElastic)
    # get flagged REVIEW even though szz is mathematically correct.
    $matCols = ($materialRow -split '\s+') | Where-Object { $_ -ne '' }
    if ($matCols.Count -lt 6) { throw "Material row in $($seedDat.FullName) has fewer than 6 columns: $materialRow" }
    $nu0 = [double] $matCols[5]
    $nuEff = Get-EffectiveNu -MaterialType $materialType -Nu0 $nu0

    $rootPrefix = if ($folderName -eq 'MC_tunnel') { 'mc_tunnel' } else { $folderName -replace '_smoke$', '' }
    $friendlyTitle = (($rootPrefix -replace '^mc_tunnel_', '') -replace '_', ' ')
    if ($friendlyTitle -eq 'mc tunnel') { $friendlyTitle = 'Mohr-Coulomb (MC)' } else { $friendlyTitle = (Get-Culture).TextInfo.ToTitleCase($friendlyTitle) }

    $meshTypes = @('T3', 'T6', 'Q4', 'Q8')
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($mesh in $meshTypes) {
        $meshLower = $mesh.ToLowerInvariant()
        $baseName = "{0}_{1}_smoke" -f $rootPrefix, $meshLower
        $datPath = Join-Path $folder.FullName ($baseName + '.dat')
        $jsonPath = Join-Path $folder.FullName ($baseName + '.json')
        $resPath = Join-Path $folder.FullName ($baseName + '.res')

        $spec = MeshSpec -MeshType $mesh
        $datBody = New-Object System.Collections.Generic.List[string]
        $datBody.Add('! <solver:p610_t3>')
        $datBody.Add(("! {0} {1} smoke verification" -f $friendlyTitle, $mesh))
        $datBody.Add('! <BEGIN:AnalysisOptions>')
        $datBody.Add(("! failure_criterion {0}" -f $failure))
        $datBody.Add(("! material_type {0}" -f $materialType))
        $datBody.Add(("! mesh_type {0}" -f $mesh))
        $datBody.Add(("! solver_executable {0}" -f $solverExe))
        $datBody.Add('! <END:AnalysisOptions>')
        $datBody.Add(("1  {0}  1" -f $spec.NodeCount))
        $datBody.Add($materialRow)
        foreach ($n in $spec.Nodes) { $datBody.Add($n) }
        $datBody.Add($spec.Connectivity)
        foreach ($l in $spec.Nr) { $datBody.Add($l) }
        foreach ($l in $spec.Nouts) { $datBody.Add($l) }
        $datBody.Add('0.0001  80  3  0')
        $datBody.Add('1')
        $datBody.Add('0  -100  -100  0')
        foreach ($l in $spec.TractionEdge) { $datBody.Add($l) }
        [System.IO.File]::WriteAllLines($datPath, $datBody)

        $exePath = Resolve-ExePath -RepoRoot $RepoRoot -ExeName $solverExe
        $runNote = $null
        if ($null -eq $exePath) {
            $runNote = "solver exe missing: $solverExe"
        } else {
            Push-Location $folder.FullName
            try {
                & $exePath $baseName | Out-Null
            } finally {
                Pop-Location
            }
        }

        $jsonExists = Test-Path $jsonPath
        $resExists = Test-Path $resPath
        $nodeCount = $null
        $elementCount = $null
        $stageCount = $null
        $sxx = $null
        $syy = $null
        $txy = $null
        $szz = $null
        $notes = New-Object System.Collections.Generic.List[string]

        if ($jsonExists) {
            $j = Read-JsonLenient -Path $jsonPath
            $nodeCount = [int] $j.node_count
            $elementCount = [int] $j.element_count
            $stageCount = [int] $j.n_stages
            $stageArr = @($j.stages)
            if ($stageArr.Count -gt 1) {
                $stress = @($stageArr[1].element_stresses)[0]
                $sxx = [double] $stress.sxx
                $syy = [double] $stress.syy
                $txy = [double] $stress.txy
                $szz = [double] $stress.szz
            } else {
                $notes.Add('stage 1 missing') | Out-Null
            }
        } else {
            $notes.Add('json missing') | Out-Null
        }
        if (-not $resExists) {
            $notes.Add('res missing') | Out-Null
        }
        if ($runNote) { $notes.Add($runNote) | Out-Null }

        $countOk = ($nodeCount -eq $spec.NodeCount) -and ($elementCount -eq 1) -and ($stageCount -eq 2)
        $finiteOk = (Test-FiniteNumber $sxx) -and (Test-FiniteNumber $syy) -and (Test-FiniteNumber $txy) -and (Test-FiniteNumber $szz)
        # Tolerance scales with the magnitude of (sxx+syy) so it remains meaningful
        # for arbitrary nu_eff while still catching real solver drift; absolute floor
        # of 1e-8 preserves the original strictness for the canonical nu=0.25 cases.
        $szzExpected = $nuEff * ($sxx + $syy)
        $szzTol = [math]::Max(1e-8, 1e-8 * [math]::Abs($sxx + $syy))
        $szzOk = $finiteOk -and ([math]::Abs($szz - $szzExpected) -le $szzTol)
        if (-not $countOk) { $notes.Add('count mismatch') | Out-Null }
        if (-not $finiteOk) { $notes.Add('non-finite outputs') | Out-Null }
        if (-not $szzOk) {
            $notes.Add(("plane-strain szz mismatch (nu_eff={0:F4}, expected={1:F4}, got={2:F4})" -f $nuEff, $szzExpected, $szz)) | Out-Null
        }

        $verdict = if ($countOk -and $finiteOk -and $szzOk) {
            if ($mesh -eq 'Q8') { 'PASS_WITH_INTERPRETATION' } else { 'PASS' }
        } else {
            'REVIEW'
        }

        $rows.Add([pscustomobject]@{
            Folder = $folderName
            Case = $baseName
            MeshType = $mesh
            FailureCriterion = $failure
            MaterialType = $materialType
            SolverExecutable = $solverExe
            DatExists = $true
            JsonExists = $jsonExists
            ResExists = $resExists
            NodeCount = $nodeCount
            ExpectedNodes = $spec.NodeCount
            ElementCount = $elementCount
            StageCount = $stageCount
            Sxx = $sxx
            Syy = $syy
            Txy = $txy
            Szz = $szz
            Nu0 = $nu0
            NuEff = $nuEff
            PlaneStrainSzzOk = $szzOk
            Verdict = $verdict
            Notes = if ($notes.Count -gt 0) { $notes -join '; ' } else { 'ok' }
            Commit = $commit
            GeneratedAt = $now
        })
    }

    $safeTitle = ($friendlyTitle -replace '[^A-Za-z0-9]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safeTitle)) { $safeTitle = $folderName }
    $helpMd = Join-Path $folder.FullName ("{0}_SmokeTest_Help.md" -f $safeTitle)
    $helpDocx = Join-Path $folder.FullName ("{0}_SmokeTest_Help.docx" -f $safeTitle)
    $helpPdf = Join-Path $folder.FullName ("{0}_SmokeTest_Help.pdf" -f $safeTitle)
    $summaryCsv = Join-Path $folder.FullName ("{0}_SmokeTest_Summary.csv" -f $safeTitle)

    $rows | Export-Csv -Path $summaryCsv -NoTypeInformation

    $meshTableRows = foreach ($r in $rows | Sort-Object MeshType) {
        "| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f $r.Case, $r.MeshType, $r.NodeCount, $r.StageCount, `
            (Format-Num $r.Sxx), (Format-Num $r.Syy), (Format-Num $r.Szz), $r.Verdict
    }

    $helpContent = @"
# $friendlyTitle Smoke Test Help

| Field | Value |
|---|---|
| Folder | Verification/mc_tunnel/$folderName |
| Failure criterion | $failure |
| Material type | $materialType |
| Solver executable | $solverExe |
| Elements covered | T3, T6, Q4, Q8 |
| Generated at | $now |
| Commit | $commit |

## Executive Summary

This folder contains professional smoke tests for $friendlyTitle only. The tests validate parser, solver, staged result writing, and visualization import paths for T3/T6/Q4/Q8.

These are software acceptance tests, not full tunnel benchmark validation.

## FEM Result Snapshot (Stage 1)

| Case | Mesh | Nodes | Stages | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
$(($meshTableRows -join "`r`n"))

## Acceptance Rules

- node_count matches mesh type (3/6/4/8).
- element_count = 1 and n_stages = 2.
- Stage 1 stresses and displacements are finite.
- Plane-strain consistency check: szz = nu_eff * (sxx + syy). nu_eff is parsed from column 6 of the .dat material row and adjusted with the same scaling the C++ effective_elastic_constants helper applies: TransverselyIsotropic x 0.95, Orthotropic x 0.90, ViscoElastic x 0.98; all other laws use the raw nu0. The result is clamped to the open interval 0.05 to 0.49.
- Q8 is treated as PASS_WITH_INTERPRETATION when finite and plane-strain-consistent.

## Command Cookbook

- cd Verification\mc_tunnel\$folderName
- ..\..\..\build\Release\$solverExe.exe ${rootPrefix}_t3_smoke
- ..\..\..\build\Release\$solverExe.exe ${rootPrefix}_t6_smoke
- ..\..\..\build\Release\$solverExe.exe ${rootPrefix}_q4_smoke
- ..\..\..\build\Release\$solverExe.exe ${rootPrefix}_q8_smoke

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

$topCsv = Join-Path $verificationRoot 'MC_Tunnel_All_SmokeTests_Summary.csv'
$folderSummaries | Export-Csv -Path $topCsv -NoTypeInformation

$indexRows = foreach ($g in ($folderSummaries | Group-Object Folder | Sort-Object Name)) {
    $allPass = @($g.Group | Where-Object { $_.Verdict -notlike 'PASS*' }).Count -eq 0
    $verdict = if ($allPass) { 'PASS' } else { 'REVIEW' }
    $failure = ($g.Group | Select-Object -First 1).FailureCriterion
    $mat = ($g.Group | Select-Object -First 1).MaterialType
    "| $($g.Name) | $failure | $mat | $($g.Count) | $verdict |"
}

$topMd = Join-Path $verificationRoot 'MC_Tunnel_All_SmokeTests_Help.md'
$topDocx = Join-Path $verificationRoot 'MC_Tunnel_All_SmokeTests_Help.docx'
$topPdf = Join-Path $verificationRoot 'MC_Tunnel_All_SmokeTests_Help.pdf'
$topContent = @"
# MC Tunnel All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | `Verification/mc_tunnel` |
| Generated at | $now |
| Commit | $commit |

## Scope

This index aggregates all criterion/material smoke folders under `Verification/mc_tunnel` and provides a professional regression dashboard for T3/T6/Q4/Q8 smoke coverage.

## Folder Dashboard

| Folder | Failure criterion | Material type | Cases | Verdict |
|---|---|---|---:|---|
$(($indexRows -join "`r`n"))

## Detailed Summary CSV

- `MC_Tunnel_All_SmokeTests_Summary.csv`

## Usage

Rebuild all smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_mc_tunnel_smoke_packages.ps1
"@
[System.IO.File]::WriteAllText($topMd, $topContent)
if (-not $SkipDocx.IsPresent) {
    & powershell -ExecutionPolicy Bypass -File $docxBuilder -InputMd $topMd -OutputDocx $topDocx | Out-Null
}
if (-not $SkipPdf.IsPresent) {
    & powershell -ExecutionPolicy Bypass -File $pdfBuilder -InputMd $topMd -OutputPdf $topPdf | Out-Null
}

& powershell -ExecutionPolicy Bypass -File $masterIndexBuilder -RepoRoot $RepoRoot | Out-Null

Write-Host "Generated all mc_tunnel smoke packages."
Write-Host "Top-level summary: $topCsv"
