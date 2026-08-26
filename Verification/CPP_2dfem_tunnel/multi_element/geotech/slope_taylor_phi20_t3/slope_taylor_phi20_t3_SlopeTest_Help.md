# Slope-Stability Test: slope_taylor_phi20_t3

| Field | Value |
|---|---|
| Folder | Verification/geotech/slope_taylor_phi20_t3 |
| Generated at | 2026-05-05 02:35:23 +05:30 |
| Commit | 4a6b63d |
| Mode | lambda_sweep |
| expected_FoS | 1.38 |
| Tolerance (% of expected_FoS) | 30 |
| Slope angle (deg) | 90.0 |
| Slope height (m) | 2 |

## Folder Verdict

**PASS** -- lambda-sweep diagnostic monotonic; all runs completed within iteration limit; first yield saturation at lambda=0.7

## Per-run Diagnostic Table

| Case | lambda | gamma_eff (kN/m^3) | max iters | max |u| (m) | max yield ratio | iters<limit | Verdict |
|---|---:|---:|---:|---:|---:|:-:|---|
| `slope_taylor_phi20_t3_lambda050` | 0.5 | 10 | 2 | 0.003138 | 0.899942 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi20_t3_lambda070` | 0.7 | 14 | 5 | 0.004394 | 0.969983 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi20_t3_lambda085` | 0.85 | 17 | 5 | 0.005335 | 0.955138 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi20_t3_lambda100` | 1 | 20 | 5 | 0.006277 | 0.991911 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi20_t3_lambda115` | 1.15 | 23 | 5 | 0.007218 | 0.994857 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi20_t3_lambda130` | 1.3 | 26 | 5 | 0.008160 | 0.995479 | True | PASS_DIAGNOSTIC |
| `slope_taylor_phi20_t3_lambda150` | 1.5 | 30 | 5 | 0.009415 | 0.992696 | True | PASS_DIAGNOSTIC |

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

- slope_taylor_phi20_t3_SlopeTest_Summary.csv
- slope_taylor_phi20_t3_SlopeTest_Help.md
- slope_taylor_phi20_t3_SlopeTest_Help.docx
- slope_taylor_phi20_t3_SlopeTest_Help.pdf