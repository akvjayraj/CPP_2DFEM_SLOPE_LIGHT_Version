# Geotech All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/geotech |
| Generated at | 2026-05-05 02:37:46 +05:30 |
| Commit | 4a6b63d |

## Scope

This index aggregates every geotechnical-specific verification under
Verification/geotech: Prandtl strip footing bearing capacity, slope
stability, and other plasticity benchmarks tied to closed-form Terzaghi /
Brinch-Hansen / Vesic / Bishop solutions.

## Folder Dashboard

| Folder | Cases | Verdict |
|---|---:|---|
| prandtl_strip_phi0_q4 | 1 | PASS |
| prandtl_strip_phi0_t3 | 1 | PASS |
| prandtl_strip_phi20_t3 | 1 | PASS |

## Detailed Summary CSV

- Geotech_All_SmokeTests_Summary.csv

## Usage

Rebuild all geotech smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_geotech_smoke_packages.ps1