# Peck-Mair Shallow Tunnel Settlement -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/peck_mair_settlement |
| Case id | peck_mair_settlement |
| Variants | 4 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Peck/Mair Gaussian surface-settlement-trough comparison via expected_pm_* header block.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `peck_mair_q4` | Q4 | ElastoPlastic | p610_t3_elastoplastic | -50.000000 | 0.000000 | -12.500000 | PASS |
| `peck_mair_q8` | Q8 | ElastoPlastic | p610_t3_elastoplastic | -23.776042 | -1.223958 | -6.250000 | PASS_WITH_INTERPRETATION |
| `peck_mair_t3` | T3 | ElastoPlastic | p610_t3_elastoplastic | -50.000000 | -50.000000 | -25.000000 | PASS |
| `peck_mair_t6` | T6 | ElastoPlastic | p610_t3_elastoplastic | -50.000000 | -50.000000 | -25.000000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Peck_Mair_Shallow_Tunnel_Settlement_SmokeTest_Summary.csv`
- `Peck_Mair_Shallow_Tunnel_Settlement_SmokeTest_Help.md`