# Twin Tunnel Interaction -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/twin_tunnel |
| Case id | twin_tunnel |
| Variants | 8 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Single-hole baseline + asymmetric twin loading; FEM-vs-FEM delta verification.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `twin_baseline_q4` | Q4 | MohrCoulomb | p610_t3_mohr_coulomb | -100.000000 | 0.000000 | -25.000000 | PASS |
| `twin_baseline_q8` | Q8 | MohrCoulomb | p610_t3_mohr_coulomb | -47.552083 | -2.447917 | -12.500000 | PASS_WITH_INTERPRETATION |
| `twin_baseline_t3` | T3 | MohrCoulomb | p610_t3_mohr_coulomb | -100.000000 | -100.000000 | -50.000000 | PASS |
| `twin_baseline_t6` | T6 | MohrCoulomb | p610_t3_mohr_coulomb | -100.000000 | -100.000000 | -50.000000 | PASS |
| `twin_q4` | Q4 | MohrCoulomb | p610_t3_mohr_coulomb | -110.000000 | 2.500000 | -26.875000 | PASS |
| `twin_q8` | Q8 | MohrCoulomb | p610_t3_mohr_coulomb | -52.227865 | -3.397135 | -13.906250 | PASS_WITH_INTERPRETATION |
| `twin_t3` | T3 | MohrCoulomb | p610_t3_mohr_coulomb | -105.000000 | -85.000000 | -47.500000 | PASS |
| `twin_t6` | T6 | MohrCoulomb | p610_t3_mohr_coulomb | -105.000000 | -85.000000 | -47.500000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Twin_Tunnel_Interaction_SmokeTest_Summary.csv`
- `Twin_Tunnel_Interaction_SmokeTest_Help.md`