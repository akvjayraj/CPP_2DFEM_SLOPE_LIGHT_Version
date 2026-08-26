# Glossary

| Term | Meaning in this application |
|------|-----------------------------|
| **Auto Refine Search** | Circular search that iteratively refines entry/exit bands |
| **Bishop Simplified** | Moment-equilibrium slice method with horizontal interslice forces (Bishop 1955) |
| **Composite follow** | Trials may follow impenetrable floors and/or drawable weak planes / bedding, then leave |
| **Critical surface** | Trial surface with the lowest convergent factor of safety for the ranking method |
| **CRS / EPSG** | Optional project coordinate system for GeoJSON map ↔ model mapping |
| **Field Stations** | Evidence pins (model X + unit stack). **Reference elevation** shifts imported Y onto the model axis. **Create soil block** builds a closed rectangle; **Commit interfaces** writes horizontal cuts; **Draw non-horizontal layer…** snaps to borehole contacts. FEM still uses ordinary layer cuts and assignments |
| **Soil block** | Closed rectangular outer boundary around Field Stations plus **Block margin**. The box top is padding, not the ground; cut the slope through the borehole collars. **Plot boreholes** is overlay only |
| **Factor of safety (FS)** | Strength reduction factor that brings the trial surface to limiting equilibrium |
| **Geological feature** | Drawable weak plane, bedding/contact, or tension-crack polyline |
| **GLE / Morgenstern–Price** | Force and moment equilibrium with a prescribed interslice force function |
| **Grid Search** | Circular search over slip-centre grids and sampled radii |
| **Heal geometry** | Merge near-coincident vertices to close small CAD gaps |
| **Impenetrable** | Material flag (bedrock): slip cannot cut through; composite search follows its top |
| **Kernel (planar map)** | Rebuildable vertices/edges/faces used for verify, booleans, and interchange |
| **k_h, k_v** | Pseudo-static seismic coefficients (horizontal / vertical) |
| **k_y** | Yield acceleration: k_h at which FS = 1 for Newmark analysis |
| **LEM** | Limit equilibrium method (method of slices) |
| **Newmark analysis** | Permanent seismic displacement estimate from k_y and ground motion |
| **Non-circular surface** | Piecewise-linear slip polyline |
| **Results Visualization** | Tab that displays FE contours, vectors and deformed shape |
| **Ru** | Pore-pressure ratio u / (γ h) applied on slice bases |
| **Slope Limits** | Left/right X bounds that restrict surface entry/exit (optional second window) |
| **Slope Search** | Circular search from entry/exit zones along the ground |
| **Spencer** | Force and moment equilibrium with constant interslice force inclination |
| **SRF** | Strength reduction factor (FE continuum analysis) |
| **Prescribed pore pressure** | Static Flow-Seepage leaf: u from WT / Ru / B-bar / grid; Terzaghi σ′ = σ + u |
| **One-way seepage transfer** | Steady State leaf: FE seepage heads → u, then optional install into SRF (not Biot) |
| **Transient dissipation** | Transient State leaf: θ-method excess u(t) on p84 / p88 (not two-way solid–fluid) |
| **Surface Options** | Dialog that chooses circular vs non-circular search method |
| **Weak plane** | Preferential discontinuity polyline for composite follow |
| **.xfem** | Native UTF-8 JSON project file (current format 1.10) |

## See also

- [Production geometry and interchange](../geometry/production-geometry.md)
- [Finite element analysis](../fem/overview.md)
