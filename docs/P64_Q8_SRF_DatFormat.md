# P64 Q8 SRF — generalized verification `.dat` format

Plane-strain slope stability (Smith & Griffiths Program 6.4): Mohr–Coulomb, 8-node serendipity quads, viscoplastic strain with explicit strength-reduction-factor (SRF) trials.

## Routing

| Line | Example | Purpose |
|------|---------|---------|
| `! <solver:p64>` | optional | GUI / `fem_cpp` routing tag (legacy MC parametric Q8) |
| `! solver_executable SRF` | optional | GUI marker: auto-selects Project Settings → Slope stability; run-time maps to `p64` or native exe |
| `! solver_executable slope_stability_Q8_drucker_prager_SRF` | optional | Native branded Q8 SRF executable (run-time) |
| First data token | `p64` | Legacy compact decks (line 1 program id) |

`fem_cpp` resolves the driver from `<solver:…>`, `solver_executable` in AnalysisOptions, or the first non-comment token.

**Native executables** (mesh × failure criterion, shared `slope_constitutive` module):

- T3/T6/Q4/Q8: `slope_stability_{Mesh}_{mohr_coulomb|drucker_prager|hoek_brown|generalized_hoek_brown}_SRF` (uniform pattern; GUI emits branded names)
- Legacy short names (`slope_stability_T3_SRF`, `slope_stability_Q8_SRF`, …) remain buildable for older scripts but are not used by the GUI router

The GUI **Run FE Analysis** and **Script Editor → Execute Script** map `solver_executable SRF` (or `srf_mode`) to `ResolveSlopeSrfRunner` → `fem_cpp p64` (T3/T6/Q4/Q8 explicit, MC/Elastic/ElastoPlastic) or `slope_stability_{Mesh}_{crit}_SRF` from `failure_criterion` and `mesh_type`.

## AnalysisOptions block

```text
! <BEGIN:AnalysisOptions>
! failure_criterion MohrCoulomb    # MohrCoulomb | DruckerPrager | HoekBrown | GeneralizedHoekBrown
! material_type Isotropic
! mesh_type Q8          # Q8 | Q4 | T6 | T3 (explicit mesh)
! mesh_source parametric          # parametric | explicit
! solver_executable SRF
! srf_mode multi                  # multi | single
! groundwater_strength effective      # effective | total (default effective)
! groundwater_u_factor 0.55         # optional α in σ' = σ + α·u (Smith compression-negative); 1 = full effective
! groundwater_axis ydown              # optional marker for Smith-native negative-y decks
! srf_F 1.0                       # required when srf_mode single
! expected_FoS 1.55               # optional verification metadata
! expected_FoS_tolerance_pct 10
! embankment_nx1 20               # optional: rebuild mesh via emb_2d_geom (skips file coords)
! embankment_nx2 10
! embankment_ny1 10
! embankment_ny2 5
! embankment_w1 20.0
! embankment_s1 20.0
! embankment_w2 20.0
! embankment_h1 10.0
! embankment_h2 5.0
! mesh_coord_y_offset 15          # optional: explicit decks with non-negative Y (srf_sweep/)
! <END:AnalysisOptions>
```

When the block is absent, the solver treats the body as **legacy parametric** (Fortran `p64.dat` layout).

## Mesh modes

### A — Parametric (compact, backward compatible)

Smith coordinates: crest `y = 0`, base `y < 0`.

```text
w1  s1  w2  h1  h2
nx1 nx2 ny1 ny2 np_types
phi  c  psi  gamma  E  nu          # one row per material type
[etype(1..nels)  if np_types > 1]
tol  limit  nsrf
F1  F2  ...  F_nsrf
```

Node count: `nn = (3*nye+2)*nx1 + 2*nye + 1 + (3*ny2+2)*nx2` with `nye = ny1+ny2`.  
Element count: `nels = nx1*nye + ny2*nx2`.  
Boundary conditions are applied by `emb_2d_bc` (not listed in the file).

### B — Explicit mesh (p610-style blocks)

```text
nels  nn  np_types
phi  c  psi  gamma  E  nu          # per material type
[etype(1..nels)  if np_types > 1]
x  y                               # nn rows, node order 1..nn (srf_sweep: Y >= 0 via mesh_coord_y_offset; coords = mean of element-local samples per node)
n1 n2 n3 [n4 n5 n6 [n7 n8]]        # nels rows (3/4/6/8 nodes by mesh_type)
nr
node  fix_x  fix_y                 # 0 = restrained, 1 = free
tol  limit
[nsrf]                             # omitted when srf_mode single
[F1  F2  ...]                      # multi mode only
nosurf                             # optional groundwater (0 = dry)
x1  y1                             # phreatic surface points (axis Y-up)
...
gam_w                              # unit weight of water (kN/m³)
```

### Groundwater block (GUI round-trip)

```text
! <BEGIN:Groundwater>
! include true
! method hydrostatic
! gamma_w 9.81
! nosurf 2
! surf
!   x1  y1
!   x2  y2
! <END:Groundwater>
```

**Ru mode** (RS2 Problem #18 style):

```text
! <BEGIN:Groundwater>
! include true
! method ru
! Ru 0.5
! gamma_w 9.81
! nosurf 0
! <END:Groundwater>
```

Optional γsat (per material, etype order) and water-pressure grid:

```text
! <BEGIN:Groundwater>
! include true
! method hydrostatic
! gamma_w 9.81
! gamma_sat 20.0  22.0
! nosurf 2
! surf
!   0.0  15.0
!   48.0  35.0
! grid_type pore_pressure
! ngrid 3
! grid
!   10.0  5.0  49.05
!   20.0  8.0  29.43
!   30.0  12.0  9.81
! <END:Groundwater>
```

**Hydrostatic:** solver uses `u = γw × h` (depth below phreatic surface). **Ru:** `u = Ru × σv` from trial total stress (Smith: σv = max(0, −σ_yy)); phreatic `surf` points are ignored. Stage-0 K0 overburden depth uses the mesh free-surface envelope `y_surf(x)` (not domain ymax). **Grid:** optional `! grid_type` / `! ngrid` / `! grid` points (pore_pressure | pressure_head | total_head) override hydrostatic `u` via IDW sampling when present. Body forces use **γsat below the WT** when `! gamma_sat g1 g2 …` is provided (one value per material, matching etype order); otherwise the dry/bulk γ from the material row. This applies for **all mesh types** served by the Core B Q8 SRF driver (`mesh_type` T3/T6/Q4/Q8) and the Core A p610 driver (T3/T6/Q4/Q8). In-situ gravity field stress in p610 integrates dry γ above the WT and γsat below. For hydrostatic, grid, **and Ru**, p610 applies K0 to Terzaghi *effective* vertical stress (bootstrap `u = Ru·γz` for Ru) before storing totals — applying K0 to total σv then subtracting u at yield zeros σ′xx when `Kx≈Ru` (Spencer–Baker / RS2 Ex14). During gravity equilibration (stage-0 K0 cleared), the Terzaghi factor is ramped with the gravity load fraction for hydrostatic, grid, and Ru so early increments remain stable. Buoyant γ′ = γ − γw is **not** used for body forces when pore pressure is applied separately (RS2-style total stress + Terzaghi). Yield strength uses Terzaghi effective stress on normal components: `σ' = σ + α·u` with α from `groundwater_u_factor` (default 1.0; Example 4 calibrated α≈0.55 vs SLOPE64). Set `groundwater_strength total` to skip pore pressure in yield (α forced to 0). JSON stages export `u_kPa`, `sxx_eff_kPa`, `syy_eff_kPa`, `sxy_eff_kPa` per element when groundwater is active.

## SRF modes

| Mode | Body tail | Behaviour |
|------|-----------|-----------|
| `multi` | `tol limit nsrf` + `F` list | Same as legacy p64: one run, `.res` table of trials |
| `single` | `tol limit` only | One trial at `srf_F` from AnalysisOptions; use one file per F (Taylor sweep) |

## Coordinates

Use **Smith y-down** in the file (match Fortran `p64.dat`). Do not remap to GUI y-up unless a future import path requests it.

## Q8 local node order

Matches `emb_2d_geom` / `shape_fun` serendipity permutation: corners and midsides as emitted by `tools/gen_p64_q8_srf.ps1`.

## Generator

```powershell
powershell -ExecutionPolicy Bypass -File tools/gen_p64_q8_srf.ps1
```

Writes `Verification/slope/p64_cpp_generalized.dat` (explicit mesh, multi-SRF) and optional `Verification/slope/srf_sweep/p64_embankment_F*.dat` (single-SRF).

## Related files

- Compact example: `Verification/slope/p64_cpp.dat`
- Reference generalized style: `Verification/CPP_2dfem_tunnel/multi_element/geotech/slope_srf_taylor_phi0_t3/*.dat`
- Solver: `library/solver/slope stability_Q8_SRF.cpp`
- I/O: `library/io/geotech_dat_io.cpp`
