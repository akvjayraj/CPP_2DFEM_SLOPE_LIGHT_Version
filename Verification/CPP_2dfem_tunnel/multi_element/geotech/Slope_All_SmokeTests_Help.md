# Slope-Stability All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/geotech (slope_* subfolders) |
| Generated at | 2026-05-05 02:35:23 +05:30 |
| Commit | 4a6b63d |

## Scope

Aggregates every Verification/geotech/slope_* folder. Two verdict paths:

- `lambda_sweep` (Option A): per-folder lambda-gravity diagnostic.
- `srf` (Option B): solver-internal strength-reduction-factor bisection
  reads `meta.srf_FoS` from the .json output.

## Folder Dashboard

| Folder | Mode | expected_FoS | Cases | Verdict |
|---|---|---:|---:|---|
| slope_srf_taylor_phi0_t3 | srf | 1 | 7 | PASS |
| slope_taylor_phi0_t3 | lambda_sweep | 1 | 7 | PASS |
| slope_taylor_phi20_t3 | lambda_sweep | 1.38 | 7 | PASS |

## Detailed Summary CSV

- Slope_All_SmokeTests_Summary.csv

## Usage

Rebuild all slope smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_slope_smoke_packages.ps1