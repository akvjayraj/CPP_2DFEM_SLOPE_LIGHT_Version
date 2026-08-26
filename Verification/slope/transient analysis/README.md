# S&G Program 8.4 — Transient consolidation (p84)

Verification project for **Flow-Seepage/Groundwater → Transient State → Transient (plane/axisymmetric)**.

## Files

| File | Purpose |
|------|---------|
| `sg_p84_transient_plane.xfem` | GUI project — plane transient consolidation (p84) |
| `expected_gui_p84_deck.dat` | Expected GUI-built p84 deck for this mesh/settings |

## Model

- Domain: rectangle `0 ≤ x ≤ 1`, `-1 ≤ y ≤ 0`
- Mesh: structured **5×5 Q4** (36 nodes, 25 elements)
- Material: homogeneous isotropic `kx = ky = 1`
- Time integration: Δt = 0.01, nstep = 150, θ = 0.5, npri = 10, nres = 31, ntime = 100, nci = 10
- Initial excess pore pressure: 100 on free nodes; 0 on drain faces
- Fixed drain faces: **top** (max-y) and **max-x** at pressure 0

## How to run

1. Open `sg_p84_transient_plane.xfem`.
2. Confirm Project Settings → Analysis Type → Flow-Seepage/Groundwater is ON, Transient State → **Transient (plane/axisymmetric)**.
3. FE Analysis → Run (routes to `fem_cpp` program **p84**).
4. Results Visualization:
   - **Force-Displacement** — node probe time-history (Node ID + Parameter: Excess Pore Pressure or Degree of Consolidation U %)
   - **Visualization** — excess pore-pressure contours with a compact time scrubber (synced to the same frames)

## Notes

- p84 needs a **structured rectangular Q4** mesh. Other meshes under this UI option auto-route to **p88**.
- Textbook `chap08/p84_1.dat` matches this geometry and BC layout.