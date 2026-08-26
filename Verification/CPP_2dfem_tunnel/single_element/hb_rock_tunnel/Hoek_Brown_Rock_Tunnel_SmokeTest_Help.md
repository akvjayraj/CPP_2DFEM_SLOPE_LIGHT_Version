# Hoek-Brown Rock Tunnel -- Tunnel Benchmark

| Field | Value |
|---|---|
| Folder | Verification/hb_rock_tunnel |
| Case id | hb_rock_tunnel |
| Variants | 7 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Description

Hoek-Brown rock tunnel including GSI 30/50/70 sensitivity sweep on T3.

## Verification flow

1. Open the GUI app and **Load Script** any `.dat` from this folder.
2. Click **Execute Script**. The matching `p610_t3_*` solver is invoked.
3. Open the **Verification** tab. The auto-comparison rows are populated from the `expected_*` header block declared in the `.dat`.

## Variants

| Case | Mesh | Failure | Solver | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---|---|---:|---:|---:|---|
| `hb_rock_tunnel_GSI30_t3` | T3 | HoekBrown | p610_t3_hoek_brown | -100.000000 | -100.000000 | -50.000000 | PASS |
| `hb_rock_tunnel_GSI50_t3` | T3 | HoekBrown | p610_t3_hoek_brown | -100.000000 | -100.000000 | -50.000000 | PASS |
| `hb_rock_tunnel_GSI70_t3` | T3 | HoekBrown | p610_t3_hoek_brown | -100.000000 | -100.000000 | -50.000000 | PASS |
| `hb_rock_tunnel_q4` | Q4 | HoekBrown | p610_t3_hoek_brown | -100.000000 | 0.000000 | -25.000000 | PASS |
| `hb_rock_tunnel_q8` | Q8 | HoekBrown | p610_t3_hoek_brown | -47.552083 | -2.447917 | -12.500000 | PASS_WITH_INTERPRETATION |
| `hb_rock_tunnel_t3` | T3 | HoekBrown | p610_t3_hoek_brown | -100.000000 | -100.000000 | -50.000000 | PASS |
| `hb_rock_tunnel_t6` | T6 | HoekBrown | p610_t3_hoek_brown | -100.000000 | -100.000000 | -50.000000 | PASS |

## Acceptance Rules

- Solver `.json` and `.res` files exist for every `.dat`.
- `node_count` matches the patch geometry (T3=3, T6=6, Q4=4, Q8=8).
- Stage 1 stresses are finite and within the declared `tolerance_kPa` of the `expected_*` header values (the GUI Verification tab reports per-row PASS/FAIL).
- Q8 results are accepted as `PASS_WITH_INTERPRETATION` per the existing smoke-test convention because the patch is degenerate at the Q8 sampling scheme.

## Generated Artifacts

- `Hoek_Brown_Rock_Tunnel_SmokeTest_Summary.csv`
- `Hoek_Brown_Rock_Tunnel_SmokeTest_Help.md`