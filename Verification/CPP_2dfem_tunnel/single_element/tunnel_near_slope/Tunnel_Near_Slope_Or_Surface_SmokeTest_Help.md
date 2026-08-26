# Tunnel Near Slope Or Surface -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/tunnel_near_slope |
| Case id | tunnel_near_slope |
| Variants | 4 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Asymmetric far-field tensor proxy for a tunnel under sloping ground surface.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `tunnel_slope_q4` | Q4 | MohrCoulomb | p610_t3_mohr_coulomb | -120.000000 | 7.500000 | -28.125000 | PASS |
| `tunnel_slope_q8` | Q8 | MohrCoulomb | p610_t3_mohr_coulomb | -56.824219 | -5.050781 | -15.468750 | PASS_WITH_INTERPRETATION |
| `tunnel_slope_t3` | T3 | MohrCoulomb | p610_t3_mohr_coulomb | -105.000000 | -65.000000 | -42.500000 | PASS |
| `tunnel_slope_t6` | T6 | MohrCoulomb | p610_t3_mohr_coulomb | -105.000000 | -65.000000 | -42.500000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Tunnel_Near_Slope_Or_Surface_SmokeTest_Summary.csv`
- `Tunnel_Near_Slope_Or_Surface_SmokeTest_Help.md`