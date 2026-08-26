# Axisymmetric simple slope — SRF + groundwater

Second axisymmetric verification project under `Verification/slope/Groundwater/`
(after **S&G p72** confined seepage). Couples **strength reduction (SRF)** with a
**hydrostatic water table** on a simple homogeneous slope whose left face lies on
the axis of symmetry (`x = r = 0`).

## Files

| File | Purpose |
|------|---------|
| `axisymmetric_simple_slope_srf_gw.xfem` | GUI project (open in GeotechSlopeApp) |
| `README.md` | This note |

## Model

- **Geometry (m):** `(0,0)–(0,15)–(18,15)–(48,35)–(66,35)–(66,0)` — same as RS2 Example 12
- **Axis of symmetry:** left face `L1` at `x = 0`
  - `seepageAxisymmetric = true` (canvas overlay / seepage options)
  - `srfAxisymmetric = true` → mechanical continuum `*_SRF_axisym` (S&G ×`r`, `εθ=u/r`)
- **Water table (hydrostatic):** `(0,15)–(18,15)–(30,23)–(48,29)–(66,32)`
- **Material:** `c' = 41.65 kPa`, `φ' = 15°`, `γ = 18.82 kN/m³` (Arai & Tagyo 1985 ex.3)
- **Analysis categories ON:** Slope stability – SRF, Flow-Seepage/Groundwater
- **GW method:** Static → Water Surfaces (not FE seepage)
- **SRF:** Multi-λ bracket search (Coupled Static → Slope SRF)

## Expected FoS

| Source | FoS |
|--------|-----|
| RS2 SSR (plane) | ~1.09 |
| Bishop (circular, plane) | ~1.12 |
| Spencer (non-circular, plane) | ~1.09 |
| App plane twin (Coupled Static, medium T3/T6) | ~1.10–1.20 |
| App **axisym continuum** T3 (`*_SRF_axisym`) | **~1.5** (164 elem / 107 nodes; critical stage `|u|max` ≈ 0.11 m) |
| App **axisym continuum** T6 (`*_SRF_axisym`) | *record after finer mesh run* |

There is no published RS2 “axisym SSR” number for Ex12. Treat plane Ex12 as the
geometry/WT twin and establish the app axisym baseline (+ optional T3→T6 mesh check).
Axisym FoS **will differ** from plane SSR ~1.09 — hoop confinement typically raises FoS
vs plane strain on this geometry (T3 app baseline ~1.5 vs plane ~1.1).

Elastic kinematics smoke (Ke ×`r`, hoop strain): `../../axisym_elastic_smoke/`.

## How to run

1. Open `axisymmetric_simple_slope_srf_gw.xfem`.
2. Confirm **Project Settings → Axisymmetric continuum SRF (mechanical)** is checked
   (`srfAxisymmetric`), and canvases show the purple **Axis of symmetry** on `x = 0`.
3. Keep Groundwater tree on **Static** (Water Surfaces). Do **not** select Steady State →
   2D Plane/axisymmetric — that routes to `p72 → SRF` (structured rectangular Q4 only).
4. Overview should show **Coupled Static → Slope SRF** with
   `slope_stability_T3_SRF_axisym` or `slope_stability_T6_SRF_axisym` (mesh-dependent).
5. Mesh tab → discretize (prefer **6-node triangles** for the T6 baseline).
6. Boundary Conditions: Fixed base (`L6`); **Roller X** on left (`L1`, `r=0`) and right (`L5`).
7. FE Analysis → Run (SRF bracket with hydrostatic `u` from the water table).
8. Record critical FoS for T3 and T6 in the table above.

## Related

- Elastic axisym smoke: `../../axisym_elastic_smoke/`
- Pure axisymmetric seepage: `../SG_p72_2D_Plane_axisymmetric/`
- Plane SRF + WT sibling: `../RS2_Example12_simple_slope_with_water_table/`
