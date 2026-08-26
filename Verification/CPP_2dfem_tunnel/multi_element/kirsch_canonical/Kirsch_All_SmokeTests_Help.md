# Kirsch All Smoke Tests Help

| Field | Value |
|---|---|
| Folder | `Verification/kirsch1` |
| Generated at | 2026-05-05 02:33:45 +05:30 |
| Commit | 4a6b63d |

## Scope

This index aggregates every Kirsch (1898) elastic-tunnel benchmark case
discovered under `Verification/kirsch*/`. Each case is auto-validated against
the plane-strain Kirsch closed form via element-centroid sampling on the
springline and crown rays, plus inward radial wall displacement.

## Folder Dashboard

| Folder | Cases | Verdict |
|---|---:|---|
| `kirsch1` | 1 | PASS |

## Detailed Summary CSV

- `Kirsch_All_SmokeTests_Summary.csv`

## Usage

Rebuild the suite:

- `powershell -ExecutionPolicy Bypass -File tools\build_all_kirsch_smoke_packages.ps1`