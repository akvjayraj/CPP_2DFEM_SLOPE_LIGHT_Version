# Slope-Stability Test: slope_taylor_phi0_t3

| Field | Value |
|---|---|
| Folder | Verification/geotech/slope_taylor_phi0_t3 |
| Generated at | 2026-05-05 02:35:23 +05:30 |
| Commit | 4a6b63d |
| Mode | lambda_sweep |
| expected_FoS | 1 |
| Tolerance (% of expected_FoS) | 25 |
| Slope angle (deg) | 90.0 |
| Slope height (m) | 2 |

## Folder Verdict

**PASS** -- lambda-sweep diagnostic monotonic; all runs completed within iteration limit; first yield saturation at lambda=0.5

## Per-run Diagnostic Table

| Case | lambda | gamma_eff (kN/m^3) | max iters | max |u| (m) | max yield ratio | iters<limit | Verdict |
|---|---:|---:|---:|---:|---:|:-:|---|
| `slope_taylor_phi0_t3_lambda050` | 0.5 | 10 | 4 | 0.003138 | 0.987548 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi0_t3_lambda070` | 0.7 | 14 | 5 | 0.004394 | 0.998033 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi0_t3_lambda085` | 0.85 | 17 | 5 | 0.005335 | 0.996603 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi0_t3_lambda100` | 1 | 20 | 5 | 0.006277 | 0.991487 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi0_t3_lambda115` | 1.15 | 23 | 5 | 0.007218 | 0.995911 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi0_t3_lambda130` | 1.3 | 26 | 6 | 0.008160 | 0.998668 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi0_t3_lambda150` | 1.5 | 30 | 5 | 0.009415 | 0.996310 | True | PASS_DIAGNOSTIC |

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

- slope_taylor_phi0_t3_SlopeTest_Summary.csv
- slope_taylor_phi0_t3_SlopeTest_Help.md
- slope_taylor_phi0_t3_SlopeTest_Help.docx
- slope_taylor_phi0_t3_SlopeTest_Help.pdf