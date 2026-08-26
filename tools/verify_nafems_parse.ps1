<#
.SYNOPSIS
  Smoke check the Verification-tab auto-trigger parse path.

.DESCRIPTION
  Loads the compiled GeotechSlopeApp.dll (release-build artifact), invokes
  P610AnalysisOptionsHelper.TryParse on each Verification/nafems/*/*.dat file
  via .NET reflection, and prints the recovered ExpectedStressBlock so we can
  confirm the same regex the live GUI uses recovers every header field. This
  is a non-interactive proxy for clicking 'Execute Script' on each NAFEMS .dat.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/verify_nafems_parse.ps1
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $DllPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $scriptDir '..')
}
if (-not $DllPath) {
    $DllPath = Join-Path $RepoRoot 'X2D_GUI\bin\Debug\net8.0-windows\GeotechSlopeApp.dll'
}

if (-not (Test-Path $DllPath)) {
    throw "GeotechSlopeApp.dll not found at $DllPath. Build the GUI first (dotnet build X2D_GUI/GeotechSlopeApp.csproj)."
}

$assembly = [System.Reflection.Assembly]::LoadFrom($DllPath)
$helperType = $assembly.GetType('GeotechSlopeApp.Backend.P610AnalysisOptionsHelper', $false, $false)
if ($null -eq $helperType) {
    throw 'P610AnalysisOptionsHelper type not found in loaded assembly.'
}
$tryParse = $helperType.GetMethod('TryParse', [System.Reflection.BindingFlags]'Public,Static')
if ($null -eq $tryParse) {
    throw 'TryParse method not found on P610AnalysisOptionsHelper.'
}

# NAFEMS suite is split across the multi/single-element category folders;
# scan both so this verifier walks every nafems_* fixture regardless of
# layout.
$nafemsScanRoots = @(
    (Join-Path $RepoRoot 'Verification\multi_element'),
    (Join-Path $RepoRoot 'Verification\single_element')
)
$datFiles = @(
    foreach ($root in $nafemsScanRoots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem -Path $root -Directory |
            Where-Object { $_.Name -like 'nafems_*' } |
            ForEach-Object { Get-ChildItem -Path $_.FullName -Filter '*.dat' -File }
    }
) | Sort-Object FullName -Unique
$nafemsRoot = ($nafemsScanRoots | Where-Object { Test-Path $_ } | Select-Object -First 1)

Write-Host ("{0,-44}  {1,-6}  {2,-6}  {3,-12}  {4,-12}  {5,-12}  {6,-12}  {7}" -f 'Case', 'Parse', 'Hdr', 'Sxx', 'Syy', 'Txy', 'Szz', 'TolKpa')
Write-Host ('-' * 130)

$totalPass = 0
$totalFail = 0
foreach ($dat in $datFiles) {
    $content = [System.IO.File]::ReadAllText($dat.FullName)
    $args = [object[]] @($content, $null)
    $ok = $tryParse.Invoke($null, $args)
    $options = $args[1]
    $expected = if ($null -ne $options) { $options.Expected } else { $null }
    $hasExpected = $null -ne $expected -and (
        $null -ne $expected.Sxx -or $null -ne $expected.Syy -or
        $null -ne $expected.Txy -or $null -ne $expected.Szz)
    $sxx = if ($null -ne $expected -and $null -ne $expected.Sxx) { [double]$expected.Sxx } else { 'n/a' }
    $syy = if ($null -ne $expected -and $null -ne $expected.Syy) { [double]$expected.Syy } else { 'n/a' }
    $txy = if ($null -ne $expected -and $null -ne $expected.Txy) { [double]$expected.Txy } else { 'n/a' }
    $szz = if ($null -ne $expected -and $null -ne $expected.Szz) { [double]$expected.Szz } else { 'n/a' }
    $tol = if ($null -ne $expected -and $null -ne $expected.TolerancekPa) { [double]$expected.TolerancekPa } else { 'n/a' }
    $line = "{0,-44}  {1,-6}  {2,-6}  {3,-12}  {4,-12}  {5,-12}  {6,-12}  {7}" -f $dat.BaseName, $ok, $hasExpected, $sxx, $syy, $txy, $szz, $tol
    if ($ok -and $hasExpected) {
        Write-Host $line -ForegroundColor Green
        $totalPass++
    } else {
        Write-Host $line -ForegroundColor Red
        $totalFail++
    }
}

Write-Host ''
Write-Host ("Parsed expected blocks for {0} / {1} NAFEMS .dat files." -f $totalPass, ($totalPass + $totalFail))
if ($totalFail -gt 0) {
    exit 1
}
