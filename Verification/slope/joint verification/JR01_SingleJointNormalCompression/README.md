# JR-01 — Single Joint Under Normal Compression

Two elastic rock blocks (1 m × 1 m each) share a horizontal Goodman joint at `y = 1` m. Uniform vertical compression is applied on the top face. Gravity is off (`γ = 0`) so the joint constitutive response is isolated.

## Analytical solution

Joint closure (not top-boundary displacement):

```
δc = σn / kn
```

with `kn = 100 MPa/m = 1.0e5 kPa/m`.

| Applied stress | Expected closure |
| -------------: | ---------------: |
|          1 MPa |          0.010 m |
|          2 MPa |          0.020 m |
|          5 MPa |          0.050 m |
|         10 MPa |          0.100 m |

Rock-block compression is `σH/E ≈ 0.67 mm` at 10 MPa and must **not** be compared to `σn/kn`.

## Sign convention (solver)

- Relative jump `un = n · (u+ − u−)`
- `un > 0` is **opening**
- Closure reported as `δc = −un`
- Normal traction `sn` is **compression-positive**: `sn = −kn · un` (before tension cutoff)

## How to run in the GUI

1. Open `jr01_single_joint_normal_compression.xfem`.
2. Mesh tab shows the baked medium T3 grid (8×16 quads, 256 triangles). Load Conditions canvas shows the 10 MPa top line load. Boundary Conditions tab shows `uy = 0` on `y = 0` and `ux = 0` at `(0, 0)`.
3. Click Mesh (T3) before Run FE so the joint line becomes a split Goodman interface. Re-apply base restraints if node IDs change.
4. Run FE (p610 T3, `layers = 0`). Default top load is 10 MPa downward.

## Headless decks

`decks/` contains p610 T3 `.dat` files for 1/2/5/10 MPa, coarse/medium/fine meshes, unload, and a small tension check. Generate or refresh them with:

```
dotnet test tests/GeotechSlopeApp.Tests.sln --filter WriteStandardDecks
```

or `python tools/write_decks.py`.

Solver: `p610_t3_elastoplastic.exe` (`layers == 0` traction path). JSON must contain `joints[]` and `reactions`.

## Acceptance

- Closure error `< 1%`
- `|kn_num − kn| / kn < 1%`
- Force equilibrium `|Fy_reaction − σB| / (σB) < 0.1%`

See `jr01_results.md` and `plots/`:

- `plot1_stress_vs_closure.svg` — σn vs δc with analytical line
- `plot2_error_vs_stress.svg` — closure error vs applied stress
- `plot3_mesh_convergence.svg` — δc at 10 MPa vs mesh density
- `plot4_load_unload.svg` — coincident loading/unloading path

CSVs in `plots/` feed `tools/make_plots.py` if matplotlib is available.
