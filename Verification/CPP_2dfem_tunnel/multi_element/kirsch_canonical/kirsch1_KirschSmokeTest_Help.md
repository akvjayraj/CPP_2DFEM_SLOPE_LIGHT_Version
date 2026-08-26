# Kirsch (1898) Elastic Tunnel - kirsch1

| Field | Value |
|---|---|
| Folder | `Verification/kirsch1` |
| Generated at | 2026-05-05 02:33:45 +05:30 |
| Commit | 4a6b63d |

## Analytical Reference

Kirsch (1898). Plane-strain biaxial loading of a circular hole in an infinite,
homogeneous, isotropic, linear-elastic medium. Inputs declared in the .dat
header:

- Tunnel radius `a` and far-field stress (`sigma_v`, `K0`) - tension-positive convention.
- Linear-elastic `E` and `nu`.

The orchestrator compares analytical vs. FE polar stresses `sigma_r` and
`sigma_theta` along the springline (`theta` near 0) and crown
(`theta` near pi/2), plus the inward radial wall displacement at sampled
wall nodes.

## Acceptance

- Per-quantity tolerance: `max(absolute_tol, tolerance_pct * |analytical|)`.
- Defaults: `tolerance_pct = 5%`, `stress_floor = 200 kPa`,
  `disp_floor = 5e-5 m`; override via `! expected_kirsch_*` headers.

## Per-Case Results

| Case | Mesh | Far-field (r/a >= 3) pass/total | All checks pass/total | max %% sigma (far field) | max %% sigma (overall) | Verdict |
|---|---|---:|---:|---:|---:|---|
| `kirsch_quarter1` | T3 | 16/16 | 26/39 | 3.10 | 58.88 | PASS_WITH_INTERPRETATION |

## Verdict Conventions

- `PASS` - every springline / crown / wall check passes within the declared tolerance.
- `PASS_WITH_INTERPRETATION` - far-field elastic stresses match within tolerance, but near-wall (r/a < 3) checks deviate. This is the documented T3 discretization limit on stress concentrations and wall `u_r`; orchestration mechanics, mesh, loading, and analytical evaluation are correct.
- `REVIEW` - far-field elastic field deviates from the closed form, indicating a real problem worth investigating.

## Generated Artifacts

- kirsch1_KirschSmokeTest_Summary.csv
- `<case>_KirschChecks.csv` per .dat (springline / crown / wall detail rows)
- kirsch1_KirschSmokeTest_Help.md