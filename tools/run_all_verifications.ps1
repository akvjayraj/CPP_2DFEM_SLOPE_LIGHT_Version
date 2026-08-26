<#
.SYNOPSIS
  Build a master verification dashboard across all verification suites.

.DESCRIPTION
  Auto-discovers suite summary CSV files under Verification/ that follow the
  convention <Suite>/<Name>_All_SmokeTests_Summary.csv and aggregates them
  into:
    - Verification/_index.csv
    - Verification/_index.md

  This script is intentionally non-destructive; it only reads existing
  verification outputs and writes the master index files.

  Adding a new suite requires no changes here: drop a folder under
  Verification/ that contains a *_All_SmokeTests_Summary.csv file matching
  the same column shape, and it appears in the master dashboard.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/run_all_verifications.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $scriptDir '..')
}

$verificationRoot = Join-Path $RepoRoot 'Verification'
if (-not (Test-Path $verificationRoot)) {
    throw "Verification folder not found: $verificationRoot"
}

function Get-CommitHash {
    param([string] $Root)
    try {
        $hash = git -C $Root rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $hash) { return $hash.Trim() }
    } catch { }
    return 'unknown'
}

function Get-SuiteRowsFromCsv {
    param(
        [string] $CsvPath,
        [string] $SuiteName
    )
    if (-not (Test-Path $CsvPath)) { return @() }
    $rows = Import-Csv -Path $CsvPath
    if ($null -eq $rows) { return @() }
    return @($rows | ForEach-Object {
        $folder = if ($_.PSObject.Properties.Name -contains 'Folder') { $_.Folder } else { '' }
        $caseName = if ($_.PSObject.Properties.Name -contains 'Case') { $_.Case } else { '' }
        $meshType = if ($_.PSObject.Properties.Name -contains 'MeshType') { $_.MeshType } else { '' }
        $failureCriterion = if ($_.PSObject.Properties.Name -contains 'FailureCriterion') { $_.FailureCriterion } else { '' }
        $materialType = if ($_.PSObject.Properties.Name -contains 'MaterialType') { $_.MaterialType } else { '' }
        $verdict = if ($_.PSObject.Properties.Name -contains 'Verdict') { $_.Verdict } else { '' }
        $notes = if ($_.PSObject.Properties.Name -contains 'Notes') { $_.Notes } else { '' }
        [pscustomobject]@{
            Suite = $SuiteName
            Folder = $folder
            Case = $caseName
            MeshType = $meshType
            FailureCriterion = $failureCriterion
            MaterialType = $materialType
            Verdict = $verdict
            Notes = $notes
        }
    })
}

# Auto-discover suite summary files. Two accepted layouts:
#   (A) legacy depth-2: Verification/<SuiteName>/<Anything>_All_SmokeTests_Summary.csv
#   (B) categorized depth-3: Verification/{multi_element|single_element}/<SuiteName>/<Anything>_All_SmokeTests_Summary.csv
# In layout (B) the dashboard "Suite" column is rendered as
# "<category>/<suite>" so the GUI loader can locate the case folder under the
# category subtree. Existing depth-2 callers continue to work unchanged.
$categoryRoots = @('multi_element','single_element')
$summaryFiles = @(Get-ChildItem -Path $verificationRoot -Recurse -Filter '*_All_SmokeTests_Summary.csv' -File -ErrorAction SilentlyContinue |
    Where-Object {
        $rel = $_.FullName.Substring($verificationRoot.Length).TrimStart('\','/')
        $parts = $rel -split '[\\/]'
        if ($parts.Length -eq 2) { return $true }
        if ($parts.Length -eq 3 -and $categoryRoots -contains $parts[0]) { return $true }
        return $false
    })

$allRows = New-Object System.Collections.Generic.List[object]

foreach ($file in $summaryFiles) {
    $rel = $file.FullName.Substring($verificationRoot.Length).TrimStart('\','/')
    $parts = $rel -split '[\\/]'
    $suiteName = if ($parts.Length -eq 3 -and $categoryRoots -contains $parts[0]) {
        # Render as 'category/suite' so the GUI loader can reconstruct the folder path.
        "{0}/{1}" -f $parts[0], $parts[1]
    } else {
        Split-Path -Parent $file.FullName | Split-Path -Leaf
    }
    foreach ($row in (Get-SuiteRowsFromCsv -CsvPath $file.FullName -SuiteName $suiteName)) {
        $allRows.Add($row) | Out-Null
    }
}

$generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$commit = Get-CommitHash -Root $RepoRoot

$indexCsvPath = Join-Path $verificationRoot '_index.csv'
$indexMdPath = Join-Path $verificationRoot '_index.md'

function Write-FileSafe {
    param([string] $Path, [string] $Content)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, $Content)
        Move-Item -Force -Path $tmp -Destination $Path
    } catch {
        # If atomic move failed (e.g., file locked), fall back to overwriting
        # the temp content into the target via a retry loop. We avoid leaving
        # the temp file behind on either path.
        try { [System.IO.File]::WriteAllText($Path, $Content) } catch {
            Write-Warning "Could not write '$Path' (file may be locked). Output written to '$tmp' instead."
            return
        }
        if (Test-Path $tmp) { Remove-Item -Force $tmp -ErrorAction SilentlyContinue }
    }
}

$csvContent =
    if ($allRows.Count -gt 0) {
        ($allRows | ConvertTo-Csv -NoTypeInformation) -join "`r`n"
    } else {
        (@([pscustomobject]@{
            Suite = ''
            Folder = ''
            Case = ''
            MeshType = ''
            FailureCriterion = ''
            MaterialType = ''
            Verdict = ''
            Notes = ''
        }) | ConvertTo-Csv -NoTypeInformation) -join "`r`n"
    }

Write-FileSafe -Path $indexCsvPath -Content $csvContent

$suiteDashRows = foreach ($group in ($allRows | Group-Object Suite | Sort-Object Name)) {
    $caseCount = @($group.Group).Count
    $problemCount = @($group.Group | Where-Object { $_.Verdict -notlike 'PASS*' }).Count
    $suiteVerdict = if ($problemCount -eq 0 -and $caseCount -gt 0) { 'PASS' } elseif ($caseCount -eq 0) { 'NO_DATA' } else { 'REVIEW' }
    "| {0} | {1} | {2} | {3} |" -f $group.Name, $caseCount, $problemCount, $suiteVerdict
}

$suiteDashBlock = if ($suiteDashRows) { $suiteDashRows -join "`r`n" } else { '| no suites | 0 | 0 | NO_DATA |' }

$discoveredFilesBlock = if (@($summaryFiles).Count -gt 0) {
    ($summaryFiles | ForEach-Object {
        $rel = $_.FullName.Substring($RepoRoot.ToString().Length).TrimStart('\','/')
        "- $rel"
    }) -join "`r`n"
} else {
    '- (no suite summaries discovered)'
}

$md = @"
# Verification Master Index

| Field | Value |
|---|---|
| Root | Verification |
| Generated at | $generatedAt |
| Commit | $commit |

## Suite Dashboard

| Suite | Cases | Non-pass cases | Verdict |
|---|---:|---:|---|
$suiteDashBlock

## Discovered Suite Summaries

$discoveredFilesBlock

## Artifacts

- _index.csv: machine-readable rollup of all discovered suite summaries.
- _index.md: human-readable dashboard for quick health checks.

## Conventions

- A new suite is auto-discovered when its top-level folder under Verification/
  contains a file named *_All_SmokeTests_Summary.csv with the standard
  Folder/Case/MeshType/FailureCriterion/MaterialType/Verdict/Notes columns.
- Run this script after rebuilding any suite to refresh the master dashboard.
"@

Write-FileSafe -Path $indexMdPath -Content $md

Write-Host "Generated verification master index:"
Write-Host " - $indexCsvPath"
Write-Host " - $indexMdPath"
