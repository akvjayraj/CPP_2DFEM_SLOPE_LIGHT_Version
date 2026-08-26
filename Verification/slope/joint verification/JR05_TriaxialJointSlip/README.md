# JR-05 — Triaxial Loading of a Jointed Rock Column

RS3 Joint Verification Manual **Case 4** (Jaeger / Deb & Das). Elastic rock, one planar Goodman joint, confinement `σ3` on the sides, vertical `σ1` on the top. Failure is joint slip.

`β` is the joint angle from the **horizontal**. Cohesions are from RS2 Stress Verification Table 27.1 (the RS3 table lists `σ3` but not `c`).

## Parameters (RS3 Table 4-1 + RS2 Table 27.1)

| Quantity | Case 1 | Case 2 |
| -------- | ------ | ------ |
| `E` | 2000 MPa | 2000 MPa |
| `ν` | 0.3 | 0.3 |
| Column | 1 m × 3 m | 1 m × 3 m |
| `σ3` | 35 MPa | 70 MPa |
| Joint `c` | 11.135 MPa | 0.2749 MPa |
| `φ` | 30° | 30° |
| `kn` | 10 GPa/m | 10 GPa/m |
| `ks` | 1 GPa/m | 1 GPa/m |
| `β` | 35, 40, 45, 50, 60, 70° | same |

The loadable `.xfem` is Case 1 at `β = 45°` with `σ1` set to the analytical failure stress (183.3 MPa).

## Analytical solution

```
σ1 = σ3 + 2(σ3 tanφ + c) / ((1 − tanφ cotβ) sin 2β)
σn = σ1 cos²β + σ3 sin²β
τ  = (σ1 − σ3) sinβ cosβ   =   c + σn tanφ   at slip
```

At `β = 45°`, Case 1: `σ1 ≈ 183.3 MPa`. Case 2: `σ1 ≈ 262.4 MPa`.

## How to run in the GUI

1. Open `jr05_triaxial_joint_slip.xfem`.
2. Click Mesh (T3) before Run FE so the 45° line becomes a split interface.
3. Run FE (p610 T3, `layers = 0`). Top load is `σ1`; side loads are `σ3`.

Headless decks for all six angles and both cases: `decks/jr05_c1_*deg.dat`, `jr05_c2_*deg.dat`.

## Acceptance

At the analytical `σ1`, `|sn − σn|` and `||τ| − (c + σn tanφ)|` within 2%.
