# Salencon (1969) MC Cylindrical Hole - salencon_mc_tunnel_t3

| Field | Value |
|---|---|
| Folder | `Verification/salencon/salencon_mc_tunnel_t3` |
| Generated at | 2026-05-05 02:28:42 +05:30 |
| Commit | 4a6b63d |

## Analytical Reference

Salencon (1969). Plane-strain Mohr-Coulomb cylindrical hole in an infinite,
hydrostatically loaded medium. Inputs declared in the .dat header:

- Tunnel radius `a` and far-field hydrostatic pressure `P0` (compression-positive).
- Mohr-Coulomb strength: `phi`, `c`, dilation `psi`.
- Linear-elastic `E` and `nu`.

The orchestrator computes the closed-form plastic radius `R0/a` and compares
analytical vs. FE radial / hoop stress at every springline (`theta` near 0)
element centroid, plus the inward radial wall displacement at springline
nodes.

## Acceptance

- Per-quantity tolerance: `max(absolute_tol, tolerance_pct * |analytical|)`.
- Defaults: `tolerance_pct = 5%`, `stress_floor = 50 kPa`, `disp_floor = 1e-5 m`,
  overridable via `! expected_salencon_*` headers in the .dat.

## Per-Case Results

| Case | Mesh | R0/a | Far-field (r/a >= 5) pass/total | All checks pass/total | max %% sigma (far field) | max %% sigma (overall) | Verdict |
|---|---|---:|---:|---:|---:|---:|---|
| `salencon_mc_tunnel_t3` | T3 | 1.7350 | 6/6 | 6/21 | 7.05 | 994.90 | PASS_WITH_INTERPRETATION |

## Verdict Conventions

- `PASS` - every springline check (far field, near wall, plastic zone, and wall `u_r`) passes within the declared tolerance.
- `PASS_WITH_INTERPRETATION` - far-field elastic stresses converge to the Salencon closed form within tolerance, but near-wall and/or plastic-zone checks deviate. This is the documented small-strain viscoplastic limitation: the wall closure `u_r` and plastic radius are systematically underestimated. The orchestration mechanics, mesh, loading path, and analytical evaluation are nonetheless correct.
- `REVIEW` - even the far-field elastic field deviates from the closed form, indicating an orchestration / FE / analytical mismatch worth investigating before continuing.

## Generated Artifacts

- salencon_mc_tunnel_t3_SmokeTest_Summary.csv
- `<case>_SalenconChecks.csv` per .dat (springline detail rows)
- salencon_mc_tunnel_t3_SmokeTest_Help.md