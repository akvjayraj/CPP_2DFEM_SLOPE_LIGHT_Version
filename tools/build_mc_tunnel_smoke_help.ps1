<#
.SYNOPSIS
  Build the MC_tunnel smoke-test help outputs.

.DESCRIPTION
  Generates:
    - Verification/mc_tunnel/MC_tunnel/MC_Tunnel_SmokeTest_Help.docx
    - Verification/mc_tunnel/MC_tunnel/MC_Tunnel_SmokeTest_Summary.csv

  The DOCX conversion reuses docs/tools/build_smoke_help_docx.ps1 so no Word,
  Pandoc, or Python installation is required.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/build_mc_tunnel_smoke_help.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $SmokeFolder,
    [string] $InputMd,
    [string] $OutputDocx,
    [string] $OutputPdf,
    [string] $OutputCsv,
    [switch] $SkipDocx,
    [switch] $SkipPdf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $scriptDir '..')
}

if (-not $SmokeFolder) {
    # Prefer the new categorized layout; fall back to the legacy path if the
    # old folder still exists (older checkouts before reorg).
    $candidates = @(
        (Join-Path $RepoRoot 'Verification\single_element\MC_tunnel'),
        (Join-Path $RepoRoot 'Verification\mc_tunnel\MC_tunnel')
    )
    $SmokeFolder = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $SmokeFolder) { $SmokeFolder = $candidates[0] }
}
if (-not $InputMd)     { $InputMd     = Join-Path $SmokeFolder 'MC_Tunnel_SmokeTest_Help.md' }
if (-not $OutputDocx)  { $OutputDocx  = Join-Path $SmokeFolder 'MC_Tunnel_SmokeTest_Help.docx' }
if (-not $OutputPdf)   { $OutputPdf   = Join-Path $SmokeFolder 'MC_Tunnel_SmokeTest_Help.pdf' }
if (-not $OutputCsv)   { $OutputCsv   = Join-Path $SmokeFolder 'MC_Tunnel_SmokeTest_Summary.csv' }

function Get-CommitHash {
    param([string] $Root)
    try {
        $hash = git -C $Root rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $hash) { return $hash.Trim() }
    } catch {
        return 'unknown'
    }
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
    # Match the lenient JSON reader used by tools/build_all_nafems_smoke_packages.ps1
    # so the smoke help build can also tolerate solver outputs containing nan/inf
    # in nodal stress fields (e.g. when a stage has a singular element).
    $raw = Get-Content -Raw $Path
    $raw = [regex]::Replace($raw, '(?i)(?<=[:\[,]\s*)-?inf(?=\s*[,}\]])', 'null')
    $raw = [regex]::Replace($raw, '(?i)(?<=[:\[,]\s*)nan(?=\s*[,}\]])', 'null')
    return ($raw | ConvertFrom-Json)
}

function Get-MeshTypeFromDat {
    param([string] $DatPath)
    if (-not (Test-Path $DatPath)) { return '' }
    foreach ($line in [System.IO.File]::ReadLines($DatPath)) {
        if ($line -match '^\s*!\s*mesh_type\s+(\S+)') {
            return $Matches[1]
        }
    }
    return ''
}

function Get-ExpectedStress {
    param([string] $MeshType)
    switch ($MeshType.ToUpperInvariant()) {
        'T3' { return [pscustomobject]@{ Sxx = -100.0; Syy = -100.0; Txy = 0.0;       Szz = -50.0; Exact = $true  } }
        'T6' { return [pscustomobject]@{ Sxx = -100.0; Syy = -100.0; Txy = 33.333333; Szz = -50.0; Exact = $true  } }
        'Q4' { return [pscustomobject]@{ Sxx = -100.0; Syy = 0.0;    Txy = 0.0;       Szz = -25.0; Exact = $true  } }
        'Q8' { return [pscustomobject]@{ Sxx = $null;   Syy = $null; Txy = $null;     Szz = $null;  Exact = $false } }
        default { return [pscustomobject]@{ Sxx = $null; Syy = $null; Txy = $null; Szz = $null; Exact = $false } }
    }
}

function Compare-Near {
    param($Actual, $Expected, [double] $Tolerance = 1e-4)
    if ($null -eq $Expected) { return $true }
    if (-not (Test-FiniteNumber $Actual)) { return $false }
    return ([math]::Abs(([double] $Actual) - ([double] $Expected)) -le $Tolerance)
}

if (-not (Test-Path $SmokeFolder)) {
    throw "Smoke folder not found: $SmokeFolder"
}
if (-not (Test-Path $InputMd)) {
    throw "Help Markdown not found: $InputMd"
}

$commit = Get-CommitHash -Root $RepoRoot
$generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$expectedNodes = @{ T3 = 3; T6 = 6; Q4 = 4; Q8 = 8 }

$rows = foreach ($dat in Get-ChildItem -Path $SmokeFolder -Filter 'mc_tunnel_*_smoke.dat' | Sort-Object Name) {
    $caseName = [System.IO.Path]::GetFileNameWithoutExtension($dat.Name)
    $jsonPath = Join-Path $SmokeFolder ($caseName + '.json')
    $resPath  = Join-Path $SmokeFolder ($caseName + '.res')
    $meshType = Get-MeshTypeFromDat -DatPath $dat.FullName
    $meshKey = $meshType.ToUpperInvariant()

    $nodeCount = $null
    $elementCount = $null
    $stageCount = $null
    $sxx = $null
    $syy = $null
    $txy = $null
    $szz = $null
    $notes = [System.Collections.Generic.List[string]]::new()

    if (Test-Path $jsonPath) {
        $json = Read-JsonLenient -Path $jsonPath
        $nodeCount = [int] $json.node_count
        $elementCount = [int] $json.element_count
        $stageCount = [int] $json.n_stages
        $stages = @($json.stages)
        if ($stages.Count -gt 1) {
            $stress = @($stages[1].element_stresses)[0]
            $sxx = [double] $stress.sxx
            $syy = [double] $stress.syy
            $txy = [double] $stress.txy
            $szz = [double] $stress.szz
        } else {
            [void] $notes.Add('stage 1 missing')
        }
    } else {
        [void] $notes.Add('json missing')
    }

    if (-not (Test-Path $resPath)) {
        [void] $notes.Add('res missing')
    }

    $exp = Get-ExpectedStress -MeshType $meshType
    $countOk = $expectedNodes.ContainsKey($meshKey) -and ($nodeCount -eq $expectedNodes[$meshKey])
    $stageOk = ($stageCount -eq 2)
    $elementOk = ($elementCount -eq 1)
    $finiteOk = (Test-FiniteNumber $sxx) -and (Test-FiniteNumber $syy) -and (Test-FiniteNumber $txy) -and (Test-FiniteNumber $szz)
    $szzOk = $finiteOk -and ([math]::Abs($szz - (0.25 * ($sxx + $syy))) -le 1e-8)

    $stressOk = if ($exp.Exact) {
        (Compare-Near $sxx $exp.Sxx) -and
        (Compare-Near $syy $exp.Syy) -and
        (Compare-Near $txy $exp.Txy 1e-3) -and
        (Compare-Near $szz $exp.Szz)
    } else {
        $finiteOk -and $szzOk
    }

    if (-not $countOk)   { [void] $notes.Add('node count mismatch') }
    if (-not $stageOk)   { [void] $notes.Add('stage count mismatch') }
    if (-not $elementOk) { [void] $notes.Add('element count mismatch') }
    if (-not $finiteOk)  { [void] $notes.Add('non-finite output') }
    if (-not $szzOk)     { [void] $notes.Add('plane-strain szz mismatch') }

    $verdict = if ($countOk -and $stageOk -and $elementOk -and $finiteOk -and $szzOk -and $stressOk) {
        if ($meshKey -eq 'Q8') { 'PASS_WITH_INTERPRETATION' } else { 'PASS' }
    } else {
        'REVIEW'
    }

    [pscustomobject]@{
        Case = $caseName
        MeshType = $meshType
        DatExists = $true
        JsonExists = (Test-Path $jsonPath)
        ResExists = (Test-Path $resPath)
        NodeCount = $nodeCount
        ExpectedNodes = if ($expectedNodes.ContainsKey($meshKey)) { $expectedNodes[$meshKey] } else { $null }
        ElementCount = $elementCount
        StageCount = $stageCount
        Sxx = $sxx
        Syy = $syy
        Txy = $txy
        Szz = $szz
        ExpectedSxx = $exp.Sxx
        ExpectedSyy = $exp.Syy
        ExpectedTxy = $exp.Txy
        ExpectedSzz = $exp.Szz
        PlaneStrainSzzOk = $szzOk
        Verdict = $verdict
        Notes = if ($notes.Count -gt 0) { $notes -join '; ' } else { 'ok' }
        Commit = $commit
        GeneratedAt = $generatedAt
    }
}

$rows | Export-Csv -Path $OutputCsv -NoTypeInformation
Write-Host "Generated $OutputCsv"

if (-not $SkipDocx.IsPresent) {
    $builder = Join-Path $RepoRoot 'docs\tools\build_smoke_help_docx.ps1'
    if (-not (Test-Path $builder)) {
        throw "DOCX builder not found: $builder"
    }
    & powershell -ExecutionPolicy Bypass -File $builder -InputMd $InputMd -OutputDocx $OutputDocx
}

if (-not $SkipPdf.IsPresent) {
    $pdfBuilder = Join-Path $RepoRoot 'tools\build_verification_pdf.ps1'
    if (-not (Test-Path $pdfBuilder)) {
        throw "PDF builder not found: $pdfBuilder"
    }
    & powershell -ExecutionPolicy Bypass -File $pdfBuilder -InputMd $InputMd -OutputPdf $OutputPdf
}
