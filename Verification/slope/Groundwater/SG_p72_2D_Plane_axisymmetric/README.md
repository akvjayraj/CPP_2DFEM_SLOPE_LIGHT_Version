# S&G Program 7.2 — 2D Plane/axisymmetric (p72)

Verification projects for **Flow-Seepage/Groundwater → Steady State → 2D Plane/axisymmetric**.

## Files

| File | Purpose |
|------|---------|
| `sg_p72_2d_plane_axisymmetric.xfem` | GUI project — **plane** seepage (`seepageAxisymmetric = false`) |
| `sg_p72_2d_axisymmetric.xfem` | GUI project — **axisymmetric** seepage (`seepageAxisymmetric = true`; x = radial) |
| `reference_sg_p72_1.dat` | Classic Smith & Griffiths `chap07/p72_1.dat` textbook plane deck |
| `reference_sg_p72_2.dat` | Classic Smith & Griffiths `chap07/p72_2.dat` textbook axisymmetric deck |
| `expected_gui_p72_deck.dat` | Hand-written plane p72 deck matching the plane GUI geometry/BC intent |
| `expected_gui_p72_axisym_deck.dat` | Same mesh/heads as the plane GUI deck, with `'axisymmetric' 'r'` |

## Model (GUI projects)

- Domain: rectangle `0 ≤ x ≤ 6`, `0 ≤ y ≤ 6` (SI length units; for axisymmetric, **x = r**)
- Mesh: structured **6×6 Q4** (49 nodes, 36 elements) — required by p72
- Material: homogeneous isotropic `kx = ky = 1`
- Fixed total heads (GUI convention):
  - **Upstream** on min-x face: `H = 50`
  - **Downstream** on max-x face: `H = 0`

## How to run

1. Open `sg_p72_2d_plane_axisymmetric.xfem` (plane) or `sg_p72_2d_axisymmetric.xfem` (axisymmetric).
2. Confirm Project Settings → Analysis Type → Flow-Seepage/Groundwater is ON, Steady State → **2D Plane/axisymmetric**.
3. For the axisymmetric file, confirm seepage **Axisymmetric** = true.
4. FE Analysis → Run (routes to `fem_cpp` program **p72**).
5. Results: nodal **total head** / pressure head / flow rates.

## Notes

- p72 needs a **structured rectangular Q4** mesh. Irregular slope meshes must use **General 2D (p74)** instead.
- Textbook `p72_1.dat` / `p72_2.dat` use different Y origins and BC layouts than these GUI projects (positive Y, full-face min-x / max-x heads from `SeepageAnalysisSettings`).
- Textbook `p72_2.dat` is a **3×5** axisymmetric mesh with a source/sink and mixed fixed heads — not the same as the GUI 6×6 Hup/Hdown case.

## Load checklist

After opening either `.xfem`, FE Analysis Overview should show:

- **Analysis mode:** Seepage / GW flow (`fem_cpp p72`) — not Off / p610
- **Groundwater:** 2D Plane/axisymmetric (enabled)
- Category **Flow-Seepage/Groundwater** ON (driven by `geometry.groundwaterEnabled = true`)
- Axisymmetric file only: Project Settings seepage **Axisymmetric** checked

Structural BC warnings can still appear; p72 uses **fixed total heads** from Project Settings seepage options (not structural supports).
