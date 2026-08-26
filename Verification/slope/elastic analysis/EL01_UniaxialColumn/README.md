# EL-01 — Uniaxial elastic column

Continuum twin of [JR-03](../../joint%20verification/JR03_SingleJointColumnAxial/). Same 1 m × 3 m column, `E`, `ν`, and top pressure, **no joint**.

Use this file to check Structural – static elasticity in the GUI before adding interfaces. σy should be uniform compression. The 1× deformed mesh stays a vertical rectangle (a few millimetres of shortening).

## Parameters

| Quantity | Value |
| -------- | ----- |
| Column | 1 m wide × 3 m high |
| Joint | none |
| `E` | 2000 MPa = `2.0e6` kPa |
| `ν` | 0.01 |
| `γ` | 0 |
| `P` | 1 MPa on `y = 3` (VerticalDown) |
| BC | `uy = 0` on `y = 0`; `ux = 0` at `(0, 0)` |
| Mesh | baked medium T3, 8×24 quads = 225 nodes, 384 triangles |
| Solver | p610 T3, `layers = 0`, gravity off |

## Analytical solution

Uniaxial stress, free sides. Plane-strain correction at `ν = 0.01` is ~0.01%, so the 1D bar formula is the target (same as RS2/RS3 / JR-03 rock strain).

```
σy = −P
σx = 0
u(y) = P y / E
```

At `P = 1` MPa:

| Station | Formula | Value |
| ------- | ------- | ----- |
| Stress | `σy` | **−1000 kPa** (tension-positive) |
| Strain | `P / E` | 0.50 mm/m |
| Mid-height `y = 1.5` m | `P y / E` | 0.750 mm |
| Top `y = 3` m | `P H / E` | **1.500 mm** |

JR-03 adds a mid-height joint jump `P / kn = 0.100` mm, so its top displacement is 1.600 mm. EL-01 must stay at 1.500 mm.

Set the σy colour range around **−1.2×10³ to 0 kPa**. A pin artifact at `(0, 0)` is expected; it must not dominate the scale (not ±10⁶).

## How to run in the GUI

1. Open `el01_uniaxial_elastic_column.xfem`.
2. Mesh, Load Conditions, and Boundary Conditions canvases show the baked grid, the 1 MPa top load, base rollers, and the fixed corner.
3. Remesh (T3) is optional — there is no joint to split. Re-apply base restraints if node IDs change.
4. Run FE (p610 T3, `layers = 0`).

## Acceptance

- `|σy + 1000| / 1000 < 2%` away from the pin
- `|u(top) − 1.500 mm| / 1.500 mm < 1%`
- 1× deformed mesh is a vertical 1×3 m rectangle (`|u|max` on the order of millimetres, not metres)

Regenerate the `.xfem` with `tools/write_xfem.ps1`.
