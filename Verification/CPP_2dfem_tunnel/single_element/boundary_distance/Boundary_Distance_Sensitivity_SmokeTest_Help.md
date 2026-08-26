# Boundary Distance Sensitivity -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/boundary_distance |
| Case id | boundary_distance |
| Variants | 12 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

3D/5D/8D far-field boundary distance variants of the Kirsch baseline patch.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `kirsch_3D_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_3D_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_3D_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_3D_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_5D_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_5D_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_5D_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_5D_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_8D_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_8D_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_8D_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_8D_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Boundary_Distance_Sensitivity_SmokeTest_Summary.csv`
- `Boundary_Distance_Sensitivity_SmokeTest_Help.md`