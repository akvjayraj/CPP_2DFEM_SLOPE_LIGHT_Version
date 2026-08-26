# .dat file format (program-independent)

**fem_cpp** needs only the dataname (e.g. `fem_cpp mydata`). The first token in the .dat file identifies which program runs. You do **not** need to write the program number (p41, p42, …) if you use an **analysis type** instead.

---

## First token: analysis type or program id

The **first token** in the .dat file can be either:

1. **Analysis type (or element+physics)** – a name that uniquely identifies the analysis. You do **not** need to write the program number.
2. **Program id** – the book program number: `p41`, `p42`, … `p118`. Still supported.

The rest of the file is unchanged: same data in the same order as that program expects.

### Optional element line (second token)

After the first token you may add an **optional element** token (e.g. `rod_2node`, `line_2node`) for documentation. Program runners that support it (e.g. **run_p41**) will read and skip this token if it is non-numeric; if the next token is numeric (e.g. `nels`), no element line is assumed. So both formats work:

- With element: `rod_1d` → next line `rod_2node` → then `4 1` …
- Without element: `rod_1d` → then `4 1` …

---

## Supported analysis types (no program number needed)

These names map to the corresponding program. Use any of the listed aliases as the first token.

### Structural: 2-node rod / line
| First token | Program |
|-------------|---------|
| `rod_1d`, `axial_rod`, `rod_axial` | p41 |
| `pin_jointed`, `pin_jointed_frame`, `truss` | p42 |

### Structural: 2-node beam
| First token | Program |
|-------------|---------|
| `beam_static`, `beam_2d`, `beam_elastic` | p43 |
| `rigid_jointed`, `frame_rigid` | p44 |
| `beam_plastic`, `frame_plastic` | p45 |
| `beam_stability`, `beam_buckling` | p46 |

### Structural: 4-node plate
| First token | Program |
|-------------|---------|
| `plate_4node`, `plate_rect` | p47 |

### Solid / plane strain / 3D
| First token | Program |
|-------------|---------|
| `plane_strain_elastic`, `axisymmetric_strain` | p51 |
| `axisymmetric_solid_8node` | p52 |
| `solid_3d_hex`, `hexahedron_3d` | p53 |
| `elastic_general`, `elastic_2d_3d` | p54 |
| `thermal_strain`, `thermal_strain_triangle` | p55 |
| `solid_3d_hex_ebe` | p56 |
| `solid_3d_umat`, `hex_umat` | p57 |

### Seepage / flow
| First token | Program |
|-------------|---------|
| `seepage_1d`, `steady_seepage_1d` | p71 |
| `seepage_2d`, `steady_seepage_4node`, `seepage_plane` | p72 |
| `seepage_free_surface`, `flow_free_surface` | p73 |
| `seepage_general`, `seepage_2d_3d` | p74 |

### Consolidation / diffusion / thermal
| First token | Program |
|-------------|---------|
| `consolidation_1d`, `consolidation_rod` | p81 |
| `consolidation_1d_settlement` | p82 |
| `consolidation_1d_explicit` | p83 |
| `transient_4node`, `transient_plane` | p84 |
| `consolidation_4node`, `consolidation_plane` | p85 |
| `consolidation_4node_2`, `consolidation_axisymmetric` | p86 |
| `consolidation_4node_ebe` | p87 |
| `consolidation_general` | p88 |
| `diffusion_convection`, `diffusion_conv_4node` | p89 |
| `diffusion_convection_theta`, `diffusion_4node_theta` | p810 |
| `thermal_conduction`, `thermal_4node` | p811 |

### Coupled / Navier–Stokes
| First token | Program |
|-------------|---------|
| `biot_1d`, `coupled_consolidation_1d` | p93 |
| `navier_stokes_steady`, `steady_navier_stokes`, `ns_8node_4node` | p91 |
| `navier_stokes_pcg`, `ns_ebe` | p92 |

### Eigenvalue
| First token | Program |
|-------------|---------|
| `eigen_beam`, `beam_eigen` | p101 |
| `eigen_plane_strain`, `plane_strain_eigen`, `eigen_4_8_node` | p102 |
| `eigen_plane_strain_4node` | p103 |
| `eigen_plane_strain_arpack` | p104 |

### Dynamics / forced vibration
| First token | Program |
|-------------|---------|
| `forced_vibration_beam`, `beam_forced_vibration` | p111 |
| `forced_vibration_plane`, `plane_forced_vibration`, `dynamics_4_8_node` | p112 |
| `forced_vibration_8node` | p113 |
| `forced_vibration_8node_2` | p114 |
| `forced_vibration_8node_complex` | p115 |
| `forced_vibration_4node`, `dynamics_4node` | p116 |
| `forced_vibration_8node_theta` | p117 |
| `elastoplastic_dynamics`, `von_mises_dynamics`, `plastic_forced_vibration` | p118 |

### Legacy program id
You can still use **p41**, **p42**, … **p118** as the first token. No change to existing .dat files that already use these.

---

## Examples

**1D rod – analysis type (no program number):**
```
rod_1d
3 1
1.0
...
```

**Pin-jointed – analysis type:**
```
pin_jointed
4 5 2 1
...
```

**Same as above using program id:**
```
p41
3 1
1.0
...
```

```
p42
4 5 2 1
...
```

---

## .dat format for elasto-plastic analysis

Two programs do elasto-plastic analysis; use the right first token and data layout.

### Option 1: Elasto-plastic beams / rigid-jointed frames (p45)

- **First token:** `beam_plastic`, `frame_plastic`, or `p45`
- **Element:** 2-node beam or beam/rod, 1D, 2D or 3D
- **Data order after first token:**

```
nels nn ndim nprops np_types
prop(1,1) ... prop(nprops,1)   [repeat for each np_types]
etype(1) ... etype(nels)       [only if np_types > 1]
gamma(1) ... gamma(nels)       [only if ndim == 3]
g_coord(1,1) ... g_coord(ndim,nn)
g_num(1,1) ... g_num(nod,nels)
nr
k  nf(1..nodof) for each restrained node (nr lines)
loaded_nodes
node(i) val(i,1) ... val(i,nodof) for each loaded node
limit  tol
incs
dload(1) ... dload(incs)
```

- **nodof:** 2 (1D), 3 (2D), 6 (3D). **nprops** = number of material/plastic properties per type.

### Option 2: Von Mises or Mohr-Coulomb plane strain dynamics (p118)

- **First token:** `elastoplastic_dynamics`, `von_mises_dynamics`, `plastic_forced_vibration`, or `p118`
- **Element:** 8-node quadrilateral, plane strain, 2 dof/node (x,y)
- **Data order after first token:**

```
nxe nye np_types
prop(1,1) ... prop(6,1)   [6 props per type; repeat for np_types]
etype(1) ... etype(nels)  [only if np_types > 1]
x_coords(1) ... x_coords(nxe+1)
y_coords(1) ... y_coords(nye+1)
dtim nstep npri nres
nr
k  nf(1) nf(2) for each restrained node (nr lines)
loaded_nodes
node(i) val_x val_y for each loaded node
```

- **npri:** output every npri steps; **nres:** node number for time history output.
- **nprops = 6** per material type. Two modes:
  - **Von Mises:** `E  nu  rho  yield  0  0` (phi=0, psi=0 → use Von Mises with yield stress).
  - **Mohr-Coulomb:** `E  nu  rho  c  phi  psi` (c = cohesion, phi = friction angle °, psi = dilation angle °; phi > 0 selects MC).

Example: **chap11/p118_mohr_coulomb.dat** — 2×1 mesh, one material with c=10⁴, φ=30°, ψ=0°.

### Other plasticity-related programs

- **p61** – Von Mises plane strain (bearing capacity): stub only.
- **p96** – Biot elastic-plastic (Mohr-Coulomb) consolidation: stub only.

Use **p45** for beam/frame plasticity (static, load increments); use **p118** for plane strain Von Mises or Mohr-Coulomb with forced vibration (explicit dynamics). The library also provides **transversely isotropic** elasticity via `fem::deemat_ti(E1, E2, nu12, nu23, G12)` for plane strain (3×3 or 4×4); programs can call it when Material Type is Transversely Isotropic.

---

## For .dat writers (GUI, scripts, export)

1. Prefer an **analysis type** as the first token (e.g. `rod_1d`, `pin_jointed`, `steady_seepage_4node`) so users do not need to know program numbers.
2. Alternatively use the **program id** (`p41`, `p42`, …).
3. After the first token, write the problem data in the same order as the corresponding program expects.

---

## Legacy .dat files (no first token)

If the file does **not** start with an analysis type or program id, either:

- Prepend one line with a valid first token (analysis type or p41, p42, …), or
- Use the old per-program executables (e.g. `p41.exe mydata`).
