# Mesh Convergence Suite -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/mesh_convergence |
| Case id | mesh_convergence |
| Variants | 12 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Coarse/medium/fine x T3/T6/Q4/Q8 of the Kirsch baseline (single-element patch resolutions).

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `kirsch_coarse_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_coarse_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_coarse_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_coarse_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_fine_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_fine_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_fine_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_fine_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_medium_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_medium_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_medium_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_medium_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Mesh_Convergence_Suite_SmokeTest_Summary.csv`
- `Mesh_Convergence_Suite_SmokeTest_Help.md`