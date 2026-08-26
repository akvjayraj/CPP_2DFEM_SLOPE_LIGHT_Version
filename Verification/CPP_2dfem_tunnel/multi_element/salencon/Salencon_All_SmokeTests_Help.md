# Salencon All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | `Verification/salencon` |
| Generated at | 2026-05-05 02:28:42 +05:30 |
| Commit | 4a6b63d |

## Scope

This index aggregates every Salencon (1969) Mohr-Coulomb cylindrical-hole
benchmark case discovered under `Verification/salencon/`. It is the M2.3
analytical-plastic verification dashboard.

## Folder Dashboard

| Folder | Cases | Verdict |
|---|---:|---|
| `salencon_mc_tunnel_t3` | 1 | PASS |

## Detailed Summary CSV

- `Salencon_All_SmokeTests_Summary.csv`

## Usage

Regenerate the suite (.dat fixtures + run + dashboard):

- `powershell -ExecutionPolicy Bypass -File tools\gen_salencon_mc_tunnel.ps1`
- `powershell -ExecutionPolicy Bypass -File tools\build_all_salencon_smoke_packages.ps1`