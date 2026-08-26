# JR-03 — Single Jointed Rock Column Under Axial Pressure

RS3 Joint Verification Manual **Case 1** (Deb & Das 2010), run in this 2D T3 / Goodman app.

A 1 m × 3 m elastic rock column has a horizontal open joint at mid-height `y = 1.5` m. Uniform vertical pressure `P = 1 MPa` is applied on the top face. Gravity is off.

This is the first RS3 joint case to compare here: the published check is the **full column displacement** `u(y)`, not only joint closure. JR-01 isolated `δc = σn / kn` with a stiff rock. Here `E` is soft enough that rock compression and the joint jump are both visible (same numbers as RS2/RS3).

## Parameters (RS3 Table 1-1)

| Quantity | Value |
| -------- | ----- |
| Column | 1 m wide × 3 m high |
| Joint | `(0, 1.5) → (1, 1.5)`, `BothOpen` |
| `E` | 2000 MPa = `2.0e6` kPa |
| `ν` | 0.01 |
| `γ` | 0 |
| `kn` | 10 GPa/m = `1.0e7` kPa/m |
| `ks` | `1.0e7` kPa/m (not used under pure normal load) |
| `φ` | 89° (stay elastic) |
| `P` | 1 MPa on `y = 3` |
| BC | `uy = 0` on `y = 0`; `ux = 0` at `(0, 0)` |

The RS3 write-up does not fix the joint height. Mid-height is used so the mesh is uniform and the Heaviside jump sits at a node row. The analytical `u(y)` is valid for any `yj`.

## Analytical solution

Deb & Das / RS3 (downward displacement magnitude):

```
u(y) = P y / E  +  H(y − yj) · (P / kn)
```

`H` is zero below the joint and `P/kn` above it.

At `P = 1 MPa`:

| Station | Formula | Value |
| ------- | ------- | ----- |
| Rock strain | `P / E` | 0.50 mm/m |
| Below joint, `y = 0.75` m | `P y / E` | 0.375 mm |
| Joint lower face | `P yj / E` | 0.750 mm |
| Joint jump `δc` | `P / kn` | 0.100 mm |
| Joint upper face | `P yj / E + P / kn` | 0.850 mm |
| Top `y = 3` m | `P H / E + P / kn` | **1.600 mm** |

Plane-strain correction at `ν = 0.01` changes `E` by about 0.02%, so the 1D bar formula is the acceptance target (same as RS2/RS3).

Sign convention matches JR-01: solver `uy` is negative under compression; reported `u` is `−uy`. Closure `δc = −un`. `sn` is compression-positive.

## How to run in the GUI

1. Open `jr03_single_joint_column_axial.xfem`.
2. Mesh tab shows the baked medium T3 grid (8×24 quads, 384 triangles). Load Conditions shows the 1 MPa top load. Boundary Conditions shows base rollers and the fixed corner.
3. Click Mesh (T3) before Run FE so the joint line becomes a split Goodman interface. Re-apply base restraints if node IDs change.
4. Run FE (p610 T3, `layers = 0`).

## Headless decks

`decks/` has p610 T3 files for 1 MPa (published) and 2 MPa (linearity), plus coarse/fine at 1 MPa. Refresh with:

```
dotnet test tests/GeotechSlopeApp.Tests.sln --filter Jr03
```

or the NUnit `WriteStandardDecks` test.

## Acceptance

- Closure error `< 1%` vs `P / kn`
- `|u(y) − analytical| / analytical < 1%` at `y = 0.75`, joint faces, `y = 2.25`, top
- Force equilibrium `|Fy − P B| / (P B) < 0.1%`

See `jr03_results.md` and [`../joint_verification_results.txt`](../joint_verification_results.txt). Source: RS3 Joint Verification Manual §1; Deb & Das (2010).
