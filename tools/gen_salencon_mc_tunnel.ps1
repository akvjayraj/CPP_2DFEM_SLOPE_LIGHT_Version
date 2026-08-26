<#
.SYNOPSIS
  Generate the Verification/multi_element/salencon/* fixtures for the Salencon (1969) MC
  cylindrical-hole closed-form benchmark.

.DESCRIPTION
  M2.3 Slice A. The geometry, material, and far-field loading are identical to
  the existing Verification/multi_element/mc_tunnel_canonical/mc_tunnel.dat fixture (a quarter-symmetry
  T3 annulus with 11 rings x 11 angles, hydrostatic 30 MPa far field, phi=30,
  c=3450 kPa, psi=30 - matches Salencon 1969 worked example 1).

  This script reads that .dat verbatim, replaces only the <BEGIN:AnalysisOptions>
  block to add ! expected_salencon_* headers, and emits one or more variants
  under Verification/multi_element/salencon/. Output files:

    Verification/multi_element/salencon/salencon_mc_tunnel_t3/salencon_mc_tunnel_t3.dat

  The .dat is the input the C++ solver consumes; the .json/.res are produced by
  build_all_salencon_smoke_packages.ps1 when the suite is run.

.NOTES
  Idempotent. Re-running overwrites existing .dat content.
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
# Source path moved during the multi/single-element reorganization. Try the
# new categorized location first, then fall back to the legacy path so older
# checkouts still work.
$srcCandidates = @(
    (Join-Path $verificationRoot 'multi_element\mc_tunnel_canonical\mc_tunnel.dat'),
    (Join-Path $verificationRoot 'mc_tunnel\mc_tunnel.dat')
)
$srcDat = $srcCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $srcDat) {
    throw "Source Salencon-style .dat not found in any of: $($srcCandidates -join '; ')"
}

$lines = Get-Content -LiteralPath $srcDat

# Salencon parameters (must mirror the actual material row in the source .dat,
# ring 1 radius a, and the equivalent far-field hydrostatic pressure in kPa).
$expectedHeaders = @(
    '! <BEGIN:AnalysisOptions>',
    '! failure_criterion MohrCoulomb',
    '! material_type Isotropic',
    '! mesh_type T3',
    '! solver_executable p610_t3_mohr_coulomb',
    '! expected_salencon_mode infinite_mc_tunnel',
    '! expected_salencon_a 1.0',
    '! expected_salencon_P0_kPa 30000',
    '! expected_salencon_Pi_kPa 0',
    '! expected_salencon_phi_deg 30',
    '! expected_salencon_c_kPa 3450',
    '! expected_salencon_psi_deg 30',
    '! expected_salencon_E_kPa 10000000',
    '! expected_salencon_nu 0.20',
    '! expected_salencon_center_x 0.0',
    '! expected_salencon_center_y 0.0',
    '! expected_salencon_tolerance_pct 8.0',
    '! expected_salencon_stress_tolerance_kPa 250',
    '! expected_salencon_disp_tolerance_m 5e-5',
    '! <END:AnalysisOptions>'
)

# Replace lines from `! <BEGIN:AnalysisOptions>` through `! <END:AnalysisOptions>`
# with $expectedHeaders, preserving everything else (description comments, mesh,
# loads, fixities).
$out = New-Object System.Collections.Generic.List[string]
$inBlock = $false
$replaced = $false
foreach ($line in $lines) {
    if (-not $replaced -and $line -match '^!\s*<BEGIN:AnalysisOptions>') {
        $inBlock = $true
        foreach ($h in $expectedHeaders) { $out.Add($h) | Out-Null }
        $replaced = $true
        continue
    }
    if ($inBlock) {
        if ($line -match '^!\s*<END:AnalysisOptions>') { $inBlock = $false }
        continue
    }
    $out.Add($line) | Out-Null
}

if (-not $replaced) {
    throw "Source .dat does not contain an <BEGIN:AnalysisOptions> block: $srcDat"
}

# Adjust the leading description block so the Salencon-suite .dat is clearly
# self-describing when read independently of mc_tunnel/.
$header = @(
    '! <solver:p610_t3>',
    '! Salencon (1969) Mohr-Coulomb cylindrical hole, infinite plane-strain medium',
    '! a=1m R=21m sigma_h=30000 kPa Pi=0 E=10000000 kPa nu=0.20 phi=30 c=3450 kPa psi=30 (associated)',
    '! M2.3 Slice A - quarter-symmetry T3 reuse of mc_tunnel geometry'
)

# Drop existing leading "!" lines until the AnalysisOptions block; replace with
# the new header.
$body = @()
$pastHeader = $false
foreach ($line in $out) {
    if (-not $pastHeader) {
        if ($line -match '^!\s*<BEGIN:AnalysisOptions>') {
            $pastHeader = $true
            $body += $line
            continue
        }
        # Skip pre-AnalysisOptions comment lines that came from mc_tunnel.dat.
        continue
    }
    $body += $line
}

$final = @($header) + $body

$outDir = Join-Path $verificationRoot 'multi_element\salencon\salencon_mc_tunnel_t3'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDat = Join-Path $outDir 'salencon_mc_tunnel_t3.dat'

[System.IO.File]::WriteAllLines($outDat, $final)
Write-Host "Generated Salencon fixture:"
Write-Host " - $outDat"
