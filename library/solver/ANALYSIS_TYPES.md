# Analysis types – categories

How to choose the right program by **type of analysis** (physics), not by element. Use the first token in your `.dat` from the tables below.

---

## 1. Structural – static elasticity

| Analysis | First token (examples) | Programs | Element |
|----------|------------------------|----------|---------|
| **1D axial rod** | `rod_1d`, `axial_rod`, `p41` | p41 | 2-node rod |
| **Pin-jointed frame / truss** | `pin_jointed`, `truss`, `p42` | p42 | 2-node rod |
| **Elastic beam** | `beam_static`, `beam_2d`, `p43` | p43 | 2-node beam |
| **Rigid-jointed frame** | `rigid_jointed`, `frame_rigid`, `p44` | p44 | 2-node beam |
| **Elasto-plastic beam / frame** | `beam_plastic`, `frame_plastic`, `p45` | p45 | 2-node beam |
| **Beam stability (buckling)** | `beam_stability`, `beam_buckling`, `p46` | p46 | 2-node beam |
| **Thin plate (4-node)** | `plate_4node`, `plate_rect`, `p47` | p47 | 4-node quad |
| **Plane / axisymmetric strain** | `plane_strain_elastic`, `axisymmetric_strain`, `p51` | p51 | Triangle or quad |
| **Axisymmetric 8-node** | `axisymmetric_solid_8node`, `p52` | p52 | 8-node quad |
| **3D solid (hex)** | `solid_3d_hex`, `hexahedron_3d`, `p53` | p53 | 8/14/20-node hex |
| **General 2D/3D elastic** | `elastic_general`, `elastic_2d_3d`, `p54` | p54 | From .dat |
| **Thermal strain** | `thermal_strain`, `thermal_strain_triangle`, `p55` | p55 | Triangle |
| **3D hex, no global assembly** | `solid_3d_hex_ebe`, `p56` | p56 | 8/14/20-node hex |
| **3D hex, UMAT-style** | `solid_3d_umat`, `hex_umat`, `p57` | p57 | 3D hex |

---

## 2. Flow – seepage / groundwater

| Analysis | First token (examples) | Programs | Element |
|----------|------------------------|----------|---------|
| **1D steady seepage** | `seepage_1d`, `steady_seepage_1d`, `p71` | p71 | 2-node line |
| **2D steady seepage (plane/axisymmetric)** | `seepage_2d`, `steady_seepage_4node`, `p72` | p72 | 4-node quad |
| **Free-surface flow** | `seepage_free_surface`, `flow_free_surface`, `p73` | p73 | 4-node quad |
| **General 2D/3D seepage** | `seepage_general`, `seepage_2d_3d`, `p74` | p74 | From .dat |

---

## 3. Consolidation / diffusion / transient

| Analysis | First token (examples) | Programs | Element |
|----------|------------------------|----------|---------|
| **1D consolidation** | `consolidation_1d`, `consolidation_rod`, `p81` | p81 | 2-node rod |
| **1D consolidation (settlement)** | `consolidation_1d_settlement`, `p82` | p82 | 2-node line |
| **1D consolidation (explicit)** | `consolidation_1d_explicit`, `p83` | p83 | 2-node rod |
| **Transient (plane/axisymmetric)** | `transient_4node`, `transient_plane`, `p84` | p84 | 4-node quad |
| **Consolidation 4-node** | `consolidation_4node`, `consolidation_plane`, `p85` | p85 | 4-node quad |
| **Consolidation 4-node (variant)** | `consolidation_4node_2`, `consolidation_axisymmetric`, `p86` | p86 | 4-node quad |
| **Consolidation 4-node (ebe)** | `consolidation_4node_ebe`, `p87` | p87 | 4-node quad |
| **General consolidation** | `consolidation_general`, `p88` | p88 | From .dat |
| **Diffusion–convection** | `diffusion_convection`, `diffusion_conv_4node`, `p89` | p89 | 4-node quad |
| **Diffusion–convection (theta)** | `diffusion_convection_theta`, `diffusion_4node_theta`, `p810` | p810 | 4-node quad |
| **Thermal conduction** | `thermal_conduction`, `thermal_4node`, `p811` | p811 | 4-node quad |

---

## 4. Coupled / fluid–structure

| Analysis | First token (examples) | Programs | Element |
|----------|------------------------|----------|---------|
| **1D Biot (coupled consolidation)** | `biot_1d`, `coupled_consolidation_1d`, `p93` | p93 | 2-node line |
| **Steady Navier–Stokes** | `navier_stokes_steady`, `steady_navier_stokes`, `p91` | p91 | 8-node vel + 4-node pressure |
| **Navier–Stokes (PCG / ebe)** | `navier_stokes_pcg`, `ns_ebe`, `p92` | p92 | 8-node + 4-node |

---

## 5. Eigenvalue (natural frequencies / buckling)

| Analysis | First token (examples) | Programs | Element |
|----------|------------------------|----------|---------|
| **Beam eigenvalues** | `eigen_beam`, `beam_eigen`, `p101` | p101 | 2-node beam |
| **Plane strain eigenvalue (4/8-node)** | `eigen_plane_strain`, `plane_strain_eigen`, `p102` | p102 | 4- or 8-node quad |
| **Plane strain eigenvalue (4-node)** | `eigen_plane_strain_4node`, `p103` | p103 | 4-node quad |
| **Plane strain eigenvalue (ARPACK)** | `eigen_plane_strain_arpack`, `p104` | p104 | 4-node quad |

---

## 6. Dynamics – forced vibration

| Analysis | First token (examples) | Programs | Element |
|----------|------------------------|----------|---------|
| **Beam forced vibration** | `forced_vibration_beam`, `beam_forced_vibration`, `p111` | p111 | 2-node beam |
| **Plane strain forced vibration (4/8-node)** | `forced_vibration_plane`, `dynamics_4_8_node`, `p112` | p112 | 4- or 8-node quad |
| **Forced vibration 8-node** | `forced_vibration_8node`, `p113` | p113 | 8-node quad |
| **Forced vibration 8-node (variant)** | `forced_vibration_8node_2`, `p114` | p114 | 8-node quad |
| **Forced vibration 8-node (complex)** | `forced_vibration_8node_complex`, `p115` | p115 | 8-node quad |
| **Forced vibration 4-node** | `forced_vibration_4node`, `dynamics_4node`, `p116` | p116 | 4-node quad |
| **Forced vibration 8-node (theta)** | `forced_vibration_8node_theta`, `p117` | p117 | 8-node quad |
| **Elasto-plastic forced vibration** | `elastoplastic_dynamics`, `von_mises_dynamics`, `p118` | p118 | 8-node quad |

---

## Quick pick by physics

| You want to… | Category | Example first token |
|--------------|----------|---------------------|
| Axial bar / rod | Structural static | `rod_1d` |
| Truss / pin-jointed | Structural static | `pin_jointed` |
| Beams / frames | Structural static | `beam_static`, `rigid_jointed` |
| Plates | Structural static | `plate_4node` |
| 2D/3D solid elasticity | Structural static | `plane_strain_elastic`, `solid_3d_hex` |
| Groundwater flow | Flow | `seepage_2d`, `seepage_1d` |
| Consolidation / pore pressure | Consolidation | `consolidation_1d`, `consolidation_4node` |
| Heat / diffusion | Consolidation / thermal | `thermal_conduction`, `diffusion_convection_theta` |
| Coupled flow–solid | Coupled | `biot_1d`, `navier_stokes_steady` |
| Natural frequencies | Eigenvalue | `eigen_beam`, `eigen_plane_strain` |
| Time-history / vibration | Dynamics | `forced_vibration_beam`, `forced_vibration_8node` |

For element-based grouping (by mesh type), see **PXX_BY_ELEMENT_TYPE.md**. For .dat format and token list, see **DAT_FORMAT.md**.
