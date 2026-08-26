# Kirsch Elastic Tunnel -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/kirsch_elastic |
| Case id | kirsch_elastic |
| Variants | 20 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Kirsch (1898) closed-form circular hole in infinite elastic medium under multiple far-field stress states.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `kirsch_direct_tensor_q4` | Q4 | Elastic | p610_t3_elastic | -150.000000 | 10.000000 | -28.000000 | PASS |
| `kirsch_direct_tensor_q8` | Q8 | Elastic | p610_t3_elastic | -70.716667 | -5.950000 | -15.333333 | PASS_WITH_INTERPRETATION |
| `kirsch_direct_tensor_t3` | T3 | Elastic | p610_t3_elastic | -130.000000 | -60.000000 | -38.000000 | PASS |
| `kirsch_direct_tensor_t6` | T6 | Elastic | p610_t3_elastic | -130.000000 | -60.000000 | -38.000000 | PASS |
| `kirsch_hydrostatic_q4` | Q4 | Elastic | p610_t3_elastic | -100.000000 | 0.000000 | -20.000000 | PASS |
| `kirsch_hydrostatic_q8` | Q8 | Elastic | p610_t3_elastic | -47.666667 | -2.333333 | -10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_hydrostatic_t3` | T3 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_hydrostatic_t6` | T6 | Elastic | p610_t3_elastic | -100.000000 | -100.000000 | -40.000000 | PASS |
| `kirsch_K0_2_q4` | Q4 | Elastic | p610_t3_elastic | -200.000000 | 0.000000 | -40.000000 | PASS |
| `kirsch_K0_2_q8` | Q8 | Elastic | p610_t3_elastic | -95.333333 | -4.666667 | -20.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_K0_2_t3` | T3 | Elastic | p610_t3_elastic | -200.000000 | -100.000000 | -60.000000 | PASS |
| `kirsch_K0_2_t6` | T6 | Elastic | p610_t3_elastic | -200.000000 | -100.000000 | -60.000000 | PASS |
| `kirsch_K0p5_q4` | Q4 | Elastic | p610_t3_elastic | -50.000000 | 0.000000 | -10.000000 | PASS |
| `kirsch_K0p5_q8` | Q8 | Elastic | p610_t3_elastic | -23.833333 | -1.166667 | -5.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_K0p5_t3` | T3 | Elastic | p610_t3_elastic | -50.000000 | -100.000000 | -30.000000 | PASS |
| `kirsch_K0p5_t6` | T6 | Elastic | p610_t3_elastic | -50.000000 | -100.000000 | -30.000000 | PASS |
| `kirsch_pure_shear_q4` | Q4 | Elastic | p610_t3_elastic | 100.000000 | 0.000000 | 20.000000 | PASS |
| `kirsch_pure_shear_q8` | Q8 | Elastic | p610_t3_elastic | 47.666667 | 2.333333 | 10.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_pure_shear_t3` | T3 | Elastic | p610_t3_elastic | 100.000000 | -100.000000 | 0.000000 | PASS |
| `kirsch_pure_shear_t6` | T6 | Elastic | p610_t3_elastic | 100.000000 | -100.000000 | 0.000000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Kirsch_Elastic_Tunnel_SmokeTest_Summary.csv`
- `Kirsch_Elastic_Tunnel_SmokeTest_Help.md`