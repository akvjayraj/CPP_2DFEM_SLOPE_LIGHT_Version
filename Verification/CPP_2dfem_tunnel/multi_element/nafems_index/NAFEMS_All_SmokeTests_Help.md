# NAFEMS All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/nafems |
| Generated at | 2026-05-04 02:05:15 +05:30 |
| Commit | 4a6b63d |

## Scope

This index aggregates every NAFEMS-style patch test under Verification/nafems and
provides a professional regression dashboard. Each subfolder contains one or
more closed-form-checked patch tests where the FEM result is compared against
an analytical reference within a per-test tolerance.

## Folder Dashboard

| Folder | Cases | Verdict |
|---|---:|---|
| nafems_le1_t3_distorted_smoke | 1 | PASS |
| nafems_le10_thick_cyl_q4 | 1 | PASS |
| nafems_le10_thick_cyl_t3 | 1 | PASS |
| nafems_le11_thick_cyl_plane_strain_q4 | 1 | PASS |
| nafems_le11_thick_cyl_plane_strain_t3 | 1 | PASS |
| nafems_patch_q4_smoke | 1 | PASS |
| nafems_patch_q8_smoke | 1 | PASS |
| nafems_patch_t3_smoke | 1 | PASS |
| nafems_patch_t6_smoke | 1 | PASS |
| nafems_pureshear_t3_smoke | 1 | PASS |

## Detailed Summary CSV

- NAFEMS_All_SmokeTests_Summary.csv

## Usage

Rebuild all NAFEMS smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_nafems_smoke_packages.ps1