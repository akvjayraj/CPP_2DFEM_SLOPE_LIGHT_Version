# Slope-Stability Test: slope_srf_taylor_phi0_t3

| Field | Value |
|---|---|
| Folder | Verification/geotech/slope_srf_taylor_phi0_t3 |
| Generated at | 2026-05-05 02:35:23 +05:30 |
| Commit | 4a6b63d |
| Mode | srf |
| expected_FoS | 1 |
| Tolerance (% of expected_FoS) | 50 |
| Slope angle (deg) | 90.0 |
| Slope height (m) | 2 |

## Folder Verdict

**PASS** -- srf_FoS_FE = 0.500 (F-at-first-plastic-yield); within 50.0%% of expected_FoS = 1.000

## Per-run Diagnostic Table

| Case | Mesh | expected_FoS | srf_FoS | max disp (m) | Verdict |
|---|---:|---:|---:|---:|---|
| `slope_srf_taylor_phi0_t3_F030` | T3 | 1 | 0.300000 | 0.006277 | PASS_DIAGNOSTIC |
| `slope_srf_taylor_phi0_t3_F050` | T3 | 1 | 0.500000 | 0.006277 | PASS_DIAGNOSTIC |
| `slope_srf_taylor_phi0_t3_F070` | T3 | 1 | 0.700000 | 0.006277 | PASS_DIAGNOSTIC |
| `slope_srf_taylor_phi0_t3_F100` | T3 | 1 | 1.000000 | 0.006277 | PASS_DIAGNOSTIC |
| `slope_srf_taylor_phi0_t3_F150` | T3 | 1 | 1.500000 | 0.006277 | PASS_DIAGNOSTIC |
| `slope_srf_taylor_phi0_t3_F200` | T3 | 1 | 2.000000 | 0.006277 | PASS_DIAGNOSTIC |
| `slope_srf_taylor_phi0_t3_F300` | T3 | 1 | 3.000000 | 0.006277 | PASS_DIAGNOSTIC |

## Acceptance Rules

### lambda_sweep mode (Option A diagnostic)

- Every lambda run must complete within the iteration limit (max iters < limit).
- max(|u|) and max yield ratio must grow monotonically with lambda.
- The first lambda where max yield ratio crosses 0.95 is reported as a soft
  upper-bound FE limit-load multiplier ("first yield saturation at lambda=...").
- Note: the small-strain viscoplastic algorithm in p610_t3_*.exe
  redistributes through nominal collapse without diverging, so the convergence
  flag alone does NOT pin the FoS sharply. Use srf-mode (Option B) for that.

### srf mode (Option B sharp FoS)

- Verdict PASS when |srf_FoS - expected_FoS| / expected_FoS <= tolerance%.
- srf_FoS is read directly from metadata.srf_FoS of the .json output.

## Generated Artifacts

- slope_srf_taylor_phi0_t3_SlopeTest_Summary.csv
- slope_srf_taylor_phi0_t3_SlopeTest_Help.md
- slope_srf_taylor_phi0_t3_SlopeTest_Help.docx
- slope_srf_taylor_phi0_t3_SlopeTest_Help.pdf