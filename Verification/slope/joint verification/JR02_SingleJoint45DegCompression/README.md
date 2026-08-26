# JR-02 — Single Joint at 45° Under Vertical Compression

Same constitutive setup as [JR-01](../JR01_SingleJointNormalCompression/README.md), except the Goodman joint dips **45° from the horizontal**.

Two elastic rock blocks share the joint line `(0, 0.5) → (1, 1.5)` (`dipAngleDeg = 45`, `BothOpen`). Uniform vertical compression is applied on `y = 2`. Gravity is off (`γ = 0`).

## Geometry

- Domain: `[0, 1] × [0, 2]` m (same envelope as JR-01)
- Lower S1: `(0,0)–(1,0)–(1,1.5)–(0,0.5)`
- Upper S2: `(0,0.5)–(1,1.5)–(1,2)–(0,2)`
- Joint set: `kn = ks = 1e5` kPa/m, `c = 0`, `φ = 0`, tension cutoff `0`
- Rock: elastic, `E = 3e7` kPa, `ν = 0.25`, `γ = 0`
- BC: `uy = 0` on `y = 0`; `ux = 0` at `(0, 0)`
- Default top load: 10 MPa `VerticalDown` on `y = 2`

## Analytical note (uniaxial vertical stress)

On a plane at 45° to the horizontal, with only `σv` (compression positive here):

```
σn = σv / 2
τ  = σv / 2
δc = σn / kn = σv / (2 kn)
```

At 10 MPa: `σn = 5 MPa`, expected closure `0.050` m. Shear slip is `τ / ks` while `c = φ = 0` (no shear strength). Rock-block compression `σH/E` must not be compared to joint closure.

Sign convention is unchanged from JR-01: `un > 0` is opening; `δc = −un`; `sn` is compression-positive.

## How to run in the GUI

1. Open `jr02_single_joint_45deg_compression.xfem`.
2. Mesh tab shows the baked medium T3 grid (8×16). The joint is the diagonal through nodes on `y = x + 0.5`. Load Conditions shows the 10 MPa top load. Boundary Conditions shows base rollers and the fixed corner.
3. Click Mesh (T3) before Run FE so the 45° line becomes a split Goodman interface. Re-apply base restraints if node IDs change.
4. Run FE (p610 T3, `layers = 0`).
