# JR-04 — Single Jointed Rock Column Under Body Force

RS3 Joint Verification Manual **Case 2**. Same 1 m × 3 m column and mid-height joint as [JR-03](../JR03_SingleJointColumnAxial/README.md), but the load is self-weight. RS3 sets unit weight to `10 MPa/m` (`γ = 1.0e4` kPa/m). No top pressure. Gravity on, `layers = 1` (gravity turn-on).

## Analytical solution

With `y` from the fixed base:

```
σ(y) = γ (H − y)
u(y) = (γ/E) (H y − y²/2)  +  H(y − yj) · γ(H − yj)/kn
```

At `γ = 10 MPa/m`, `H = 3` m, `yj = 1.5` m, `E = 2000` MPa, `kn = 10 GPa/m`:

| Quantity | Value |
| -------- | ----- |
| Joint overburden `γ(H − yj)` | 15 MPa |
| Joint jump `δc` | 1.50 mm |
| Rock shortening at top `γ H² / (2E)` | 22.50 mm |
| Top displacement | **24.00 mm** |

## How to run in the GUI

1. Open `jr04_single_joint_column_bodyforce.xfem`.
2. Click Mesh (T3) before Run FE so the joint splits.
3. Run FE (p610 T3, `layers = 1`, gravity on).

## Acceptance

- Closure and `sn` within 2% of `γ(H − yj)/kn` and `γ(H − yj)`
- `u(y)` within 2% of the formula above
- `|Fy − γ H B| / (γ H B) < 3%` (base reaction omits a small interface share)
