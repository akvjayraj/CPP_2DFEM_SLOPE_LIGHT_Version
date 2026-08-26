# MC Tunnel All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | Verification/mc_tunnel |
| Generated at | 2026-05-04 23:54:44 +05:30 |
| Commit | 4a6b63d |

## Scope

This index aggregates all criterion/material smoke folders under Verification/mc_tunnel and provides a professional regression dashboard for T3/T6/Q4/Q8 smoke coverage.

## Folder Dashboard

| Folder | Failure criterion | Material type | Cases | Verdict |
|---|---|---|---:|---|
| MC_tunnel | MohrCoulomb | Isotropic | 4 | PASS |
| mc_tunnel_drucker_prager_smoke | DruckerPrager | Isotropic | 4 | PASS |
| mc_tunnel_generalized_hoek_brown_smoke | GeneralizedHoekBrown | Isotropic | 4 | PASS |
| mc_tunnel_hoek_brown_smoke | HoekBrown | Isotropic | 4 | PASS |
| mc_tunnel_nonlinear_smoke | ElastoPlastic | NonlinearIsotropic | 4 | PASS |
| mc_tunnel_plastic_smoke | ElastoPlastic | Plastic | 4 | PASS |
| mc_tunnel_viscoelastic_smoke | ElastoPlastic | ViscoElastic | 4 | PASS |

## Detailed Summary CSV

- MC_Tunnel_All_SmokeTests_Summary.csv

## Usage

Rebuild all smoke packages:

- powershell -ExecutionPolicy Bypass -File tools\build_all_mc_tunnel_smoke_packages.ps1