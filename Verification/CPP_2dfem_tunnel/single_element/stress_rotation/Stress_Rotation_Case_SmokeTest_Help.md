# Stress Rotation Case -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/stress_rotation |
| Case id | stress_rotation |
| Variants | 16 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Rotated principal-stress tensor at alpha = 0/15/30/45 degrees x T3/T6/Q4/Q8.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `kirsch_rot0_q4` | Q4 | Elastic | p610_t3_elastic | -200.000000 | 0.000000 | -40.000000 | PASS |
| `kirsch_rot0_q8` | Q8 | Elastic | p610_t3_elastic | -95.333333 | -4.666667 | -20.000000 | PASS_WITH_INTERPRETATION |
| `kirsch_rot0_t3` | T3 | Elastic | p610_t3_elastic | -200.000000 | -100.000000 | -60.000000 | PASS |
| `kirsch_rot0_t6` | T6 | Elastic | p610_t3_elastic | -200.000000 | -100.000000 | -60.000000 | PASS |
| `kirsch_rot15_q4` | Q4 | Elastic | p610_t3_elastic | -193.301270 | -12.500000 | -41.160254 | PASS |
| `kirsch_rot15_q8` | Q8 | Elastic | p610_t3_elastic | -93.119439 | -1.447863 | -18.913460 | PASS_WITH_INTERPRETATION |
| `kirsch_rot15_t3` | T3 | Elastic | p610_t3_elastic | -218.301270 | -131.698730 | -70.000000 | PASS |
| `kirsch_rot15_t6` | T6 | Elastic | p610_t3_elastic | -218.301270 | -131.698730 | -70.000000 | PASS |
| `kirsch_rot30_q4` | Q4 | Elastic | p610_t3_elastic | -175.000000 | -21.650635 | -39.330127 | PASS |
| `kirsch_rot30_q8` | Q8 | Elastic | p610_t3_elastic | -85.112633 | 1.221072 | -16.778312 | PASS_WITH_INTERPRETATION |
| `kirsch_rot30_t3` | T3 | Elastic | p610_t3_elastic | -218.301270 | -168.301270 | -77.320508 | PASS |
| `kirsch_rot30_t6` | T6 | Elastic | p610_t3_elastic | -218.301270 | -168.301270 | -77.320508 | PASS |
| `kirsch_rot45_q4` | Q4 | Elastic | p610_t3_elastic | -150.000000 | -25.000000 | -35.000000 | PASS |
| `kirsch_rot45_q8` | Q8 | Elastic | p610_t3_elastic | -73.458333 | 2.625000 | -14.166667 | PASS_WITH_INTERPRETATION |
| `kirsch_rot45_t3` | T3 | Elastic | p610_t3_elastic | -200.000000 | -200.000000 | -80.000000 | PASS |
| `kirsch_rot45_t6` | T6 | Elastic | p610_t3_elastic | -200.000000 | -200.000000 | -80.000000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Stress_Rotation_Case_SmokeTest_Summary.csv`
- `Stress_Rotation_Case_SmokeTest_Help.md`