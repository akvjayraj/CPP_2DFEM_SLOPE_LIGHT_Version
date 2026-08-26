<#
.SYNOPSIS
  Convert RS2 Example 1 Q8/p64 .dat decks to native p610_t3 SRF decks.

.DESCRIPTION
  Uses the existing canonical T3 RS2 Example 1 deck as a mesh/template source
  and rewrites Q8 decks under Verification/slope/RS2_Example01_simple_slope
  in-place as T3 decks, preserving each source deck's material row and SRF
  trial value when available.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/convert_rs2_example01_dat_to_t3.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $TargetDir,
    [string] $TemplateDat
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

if (-not $TargetDir) {
    $TargetDir = Join-Path $RepoRoot 'Verification\slope\RS2_Example01_simple_slope'
}
if (-not $TemplateDat) {
    $TemplateDat = Join-Path $TargetDir 'rs2_example01_simple_slope_t3_F100.dat'
}

if (-not (Test-Path $TargetDir)) { throw "Target directory not found: $TargetDir" }
if (-not (Test-Path $TemplateDat)) { throw "Template T3 deck not found: $TemplateDat" }

function Get-FirstDataLines {
    param([string] $Content)
    return @(
        $Content -split "`r?`n" |
            Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('!') }
    )
}

function Get-MeshType {
    param([string] $Content)
    if ($Content -match '(?m)^\s*!\s*mesh_type\s+(\S+)') { return $Matches[1].Trim().ToUpperInvariant() }
    return ''
}

function Get-MaterialRow {
    param([string] $Content)
    $data = Get-FirstDataLines $Content
    if ($data.Count -lt 2) { throw "Could not find material row." }
    $parts = @($data[1].Trim() -split '\s+')
    if ($parts.Count -lt 6) { throw "Material row has fewer than 6 values: $($data[1])" }
    $phi = [double]::Parse($parts[0], [Globalization.CultureInfo]::InvariantCulture)
    $c = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
    $psi = [double]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture)
    $gamma = [double]::Parse($parts[3], [Globalization.CultureInfo]::InvariantCulture)
    $e = [double]::Parse($parts[4], [Globalization.CultureInfo]::InvariantCulture)
    $nu = [double]::Parse($parts[5], [Globalization.CultureInfo]::InvariantCulture)
    $k0 = if ($parts.Count -ge 7) { [double]::Parse($parts[6], [Globalization.CultureInfo]::InvariantCulture) } else { 0.5 }
    return [pscustomobject]@{
        Phi = $phi; C = $c; Psi = $psi; Gamma = $gamma; E = $e; Nu = $nu; K0 = $k0
        Row = ('{0:G15}  {1:G15}  {2:G15}  {3:G15}  {4:G15}  {5:G15}  {6:G15}' -f $phi,$c,$psi,$gamma,$e,$nu,$k0)
    }
}

function Get-SrfF {
    param([string] $Content, [string] $FileName)
    if ($Content -match '(?m)^\s*!\s*srf_F\s+([0-9\.Ee\+\-]+)') {
        return [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($FileName -match '_F(\d+)\.dat$') {
        return ([double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) / 100.0)
    }
    $data = Get-FirstDataLines $Content
    if ($data.Count -ge 2) {
        $last = @($data[-1].Trim() -split '\s+') | Where-Object { $_ -ne '' }
        $nonNumeric = @($last | Where-Object { $_ -notmatch '^[0-9\.Ee\+\-]+$' })
        if ($last.Count -gt 0 -and $nonNumeric.Count -eq 0) {
            if ($last -contains '1' -or $last -contains '1.0') { return 1.0 }
            return [double]::Parse($last[0], [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    return 1.0
}

function Convert-ToT3Deck {
    param(
        [string] $SourcePath,
        [string] $SourceContent,
        [string] $TemplateContent
    )

    $mat = Get-MaterialRow $SourceContent
    $f = Get-SrfF $SourceContent ([System.IO.Path]::GetFileName($SourcePath))
    $fileName = [System.IO.Path]::GetFileName($SourcePath)
    $rel = $SourcePath.Substring($TargetDir.Length).TrimStart('\', '/')

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('! <solver:p610_t3>') | Out-Null
    $lines.Add("! RS2 Example 1 converted from p64/Q8 to native T3: $rel") | Out-Null
    $lines.Add('! Source geometry: w1=20, s1=20, w2=10, h1=10, h2=5 m') | Out-Null
    $lines.Add(('! Source material: phi={0:G15} c={1:G15} psi={2:G15} gamma={3:G15} E={4:G15} nu={5:G15}' -f $mat.Phi,$mat.C,$mat.Psi,$mat.Gamma,$mat.E,$mat.Nu)) | Out-Null
    $lines.Add(('! Strength reduction trial: srf_F={0:G15}' -f $f)) | Out-Null
    $lines.Add('! <BEGIN:AnalysisOptions>') | Out-Null
    $lines.Add('! failure_criterion MohrCoulomb') | Out-Null
    $lines.Add('! material_type Isotropic') | Out-Null
    $lines.Add('! mesh_type T3') | Out-Null
    $lines.Add('! mesh_source explicit') | Out-Null
    $lines.Add('! solver_executable slope_stability_T3_SRF') | Out-Null
    $lines.Add('! srf_mode single') | Out-Null
    $lines.Add(('! srf_F {0:G15}' -f $f)) | Out-Null
    $lines.Add('! expected_FoS 0.99') | Out-Null
    $lines.Add('! expected_FoS_tolerance_pct 10') | Out-Null
    $lines.Add('! expected_FoS_mode srf') | Out-Null
    $lines.Add('! slope_geometry rs2_example01_simple_slope_t3') | Out-Null
    $lines.Add('! slope_angle_deg 26.5650511771') | Out-Null
    $lines.Add('! slope_height_m 10') | Out-Null
    $lines.Add('! <END:AnalysisOptions>') | Out-Null
    $lines.Add('! Geostatic stress: legacy K0*gamma*depth (free surface auto at ymax for y-up mesh).') | Out-Null

    $templateData = Get-FirstDataLines $TemplateContent
    if ($templateData.Count -lt 4) { throw "Template has too few data lines." }
    $lines.Add($templateData[0].Trim()) | Out-Null
    $lines.Add($mat.Row) | Out-Null
    for ($i = 2; $i -lt $templateData.Count; $i++) {
        $lines.Add($templateData[$i].TrimEnd()) | Out-Null
    }

    return (($lines -join [Environment]::NewLine) + [Environment]::NewLine)
}

$template = Get-Content -Raw -Path $TemplateDat
$converted = New-Object System.Collections.Generic.List[object]

Get-ChildItem -Path $TargetDir -Recurse -File -Filter '*.dat' |
    Where-Object { $_.FullName -notlike '*\t3_srf_compare\*' } |
    ForEach-Object {
        $content = Get-Content -Raw -Path $_.FullName
        $mesh = Get-MeshType $content
        if ($mesh -eq 'T3') { return }
        if ($mesh -ne 'Q4' -and $mesh -ne 'Q8') { return }

        $newContent = Convert-ToT3Deck -SourcePath $_.FullName -SourceContent $content -TemplateContent $template
        Set-Content -Path $_.FullName -Value $newContent -NoNewline
        $converted.Add([pscustomobject]@{
            Path = $_.FullName.Substring($TargetDir.Length).TrimStart('\', '/')
            OldMesh = $mesh
            NewMesh = 'T3'
        }) | Out-Null
    }

Write-Host ("Converted {0} .dat file(s) to T3." -f $converted.Count)
$converted | Format-Table -AutoSize
