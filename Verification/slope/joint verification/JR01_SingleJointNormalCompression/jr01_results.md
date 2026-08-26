# JR-01 results — Single Joint Normal Loading

**STATUS: PASS**

Solver: `p610_t3_elastoplastic`, `layers = 0`, medium mesh 8×16 quads (256 T3).  
`kn = 1.0e5 kPa/m`. Analytical closure `δc = σn / kn` is computed independently of the solver.

Sign convention: `un > 0` is opening; reported closure `δc = −un`; `sn` is compression-positive.

## Load levels (medium mesh)

| Applied Stress | Joint Stress (avg) | Analytical Closure | Numerical Closure (avg / min / max) | kn_num | Closure Error | Force Equil. Error | STATUS |
| -------------: | -----------------: | -----------------: | ----------------------------------: | -----: | ------------: | -----------------: | :----: |
|          1 MPa |            1000 kPa |            0.010 m |              0.010 / 0.010 / 0.010 m | 1.0e5 |         0.00% |              0.00% |  PASS  |
|          2 MPa |            2000 kPa |            0.020 m |              0.020 / 0.020 / 0.020 m | 1.0e5 |         0.00% |              0.00% |  PASS  |
|          5 MPa |            5000 kPa |            0.050 m |              0.050 / 0.050 / 0.050 m | 1.0e5 |         0.00% |              0.00% |  PASS  |
|         10 MPa |           10000 kPa |            0.100 m |              0.100 / 0.100 / 0.100 m | 1.0e5 |         0.00% |              0.00% |  PASS  |

`F_applied = σn · B` with `B = 1 m`. Recovered base reaction `Fy` equals `F_applied` at every load level.

## Linearity

σn versus δc is a straight line through the origin. Fitted slope `k_fit = 100 MPa/m`, `R² = 1`.

## Mesh convergence at 10 MPa

| Mesh   | Interfaces | Analytical | Numerical | Error % |
| ------ | ---------: | ---------: | --------: | ------: |
| Coarse |          4 |    0.100 m |   0.100 m |    0.00 |
| Medium |          8 |    0.100 m |   0.100 m |    0.00 |
| Fine   |         16 |    0.100 m |   0.100 m |    0.00 |

Joint constitutive response is mesh-independent.

## Loading / unloading

Independent solves at 0 → 1 → 2 → 5 → 10 → 5 → 2 → 1 → 0 MPa. Loading and unloading closures coincide. At zero load, `δc = 0` (no permanent closure).

## Tension / opening

After a small upward traction (100 kPa), `un > 0` (opening) and `sn ≈ 0` (zero tensile strength). Subtest applicable; the existing Mohr–Coulomb tension cutoff is used. The elastic `kn` term remains in the tangent, so the opening magnitude is still ~`σ/kn` for this small load.

## Automated tests

`tests/GeotechSlopeApp.Tests.Core/Verification/Jr01SingleJointNormalLoadTests.cs`

- `SingleJointNormalLoad_{1,2,5,10}MPa`
- `SingleJointNormalLoad_LinearResponse`
- `SingleJointNormalLoad_ForceEquilibrium`
- `SingleJointNormalLoad_LoadUnload`
- `SingleJointNormalLoad_MeshConvergence`
- `SingleJointNormalLoad_TensionCutoff_OpensWithoutTension`

All 12 JR-01 tests: **Passed**.
