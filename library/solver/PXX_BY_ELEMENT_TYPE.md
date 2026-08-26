# PXX .dat files by element type

Categorization of book programs (p41–p118) by the **element type** used in their .dat files. Use this to choose the right program for a given mesh/element or to group .dat files by element.

---

## 1. Two-node rod / line (1D or pin-jointed)

| Program | Description | Notes |
|---------|-------------|--------|
| **p41** | 1D axial rod | 2-node rod, 1 dof/node (axial) |
| **p42** | Pin-jointed frame | 2-node rod, 2D or 3D (nodof = ndim) |
| **p71** | 1D steady seepage | 2-node “rod” (conductivity) |
| **p81** | 1D consolidation | 2-node rod, theta method |
| **p82** | 1D consolidation (settlement) | 2-node line, theta method |
| **p83** | 1D consolidation, explicit | 2-node rod, lumped mass |
| **p93** | 1D coupled consolidation (Biot) | 2-node line |

**Element:** 2 nodes, line; 1 dof/node (p41, p71, p81, p82, p83) or 2–3 dofs/node (p42, p93).

---

## 2. Two-node beam

| Program | Description | Notes |
|---------|-------------|--------|
| **p43** | Elastic beams | 2-node beam, 4 dof/element (2 dof/node) |
| **p44** | Rigid-jointed frames | 2-node beam/rod, 2D or 3D |
| **p45** | Elasto-plastic beams / rigid-jointed | 2-node beam or beam/rod |
| **p46** | Beam stability (buckling) | 2-node beam, optional foundation |
| **p101** | Beam eigenvalue | 2-node beam, lumped mass |
| **p111** | Beam forced vibration | 2-node beam, Newmark |

**Element:** 2 nodes, beam (flexure + axial); typically 4 dof/element (2 dof/node: translation + rotation).

---

## 3. Four-node quadrilateral (2D)

| Program | Description | Notes |
|---------|-------------|--------|
| **p47** | Plates | 4-node rectangular plate, 4 dof/node |
| **p72** | Steady seepage (plane/axisymmetric) | 4-node quad |
| **p73** | Free-surface flow | 4-node quad |
| **p84** | Plane/axisymmetric transient | 4-node |
| **p85** | Consolidation (plane/axisymmetric) | 4-node quad, theta |
| **p86** | Consolidation (plane/axisymmetric) | 4-node quad |
| **p87** | Consolidation, ebe product | 4-node quad |
| **p89** | Diffusion-convection (self-adjoint) | 4-node quad |
| **p810** | Diffusion-convection (theta) | 4-node quad |
| **p811** | Thermal conduction (plane/axisymmetric) | 4-node quad |
| **p103** | Eigenvalue, plane strain | 4-node quad |
| **p104** | Eigenvalue, plane strain (ARPACK-style) | 4-node quad |
| **p116** | Forced vibration, plane strain | 4-node quad, mixed lumped/consistent |

**Element:** 4-node quadrilateral; 1–4 dofs/node depending on physics (seepage, elasticity, plate, etc.).

---

## 4. Four- and eight-node quadrilateral (2D, configurable or mixed)

| Program | Description | Notes |
|---------|-------------|--------|
| **p51** | Plane/axisymmetric strain | 3/6/10/15-node triangle or 4/8/9-node quad (read from .dat) |
| **p102** | Eigenvalue, plane strain | 4- or 8-node quad |
| **p112** | Forced vibration, plane strain | 4- or 8-node quad, lumped mass |

**Element:** Quadrilateral with 4 or 8 nodes (and in p51, triangles too); choice often in .dat or mesh.

---

## 5. Eight-node quadrilateral (2D)

| Program | Description | Notes |
|---------|-------------|--------|
| **p91** | Steady Navier–Stokes | 8-node velocity + 4-node pressure (mixed u-p-v) |
| **p92** | Navier–Stokes, element-by-element PCG | 8-node velocity + 4-node pressure |
| **p113** | Forced vibration, plane strain | 8-node quad, lumped mass |
| **p114** | Forced vibration, plane strain | 8-node quad |
| **p115** | Forced vibration, plane strain (complex) | 8-node quad |
| **p117** | Forced vibration, plane strain, theta | 8-node quad |
| **p118** | Elastoplastic forced vibration (Von Mises) | 8-node quad, lumped mass |

**Element:** 8-node quadrilateral (serendipity); 2–3 dofs/node. p91/p92 use mixed 8-node velocity + 4-node pressure.

---

## 6. Triangles (2D, 3/6/10/15-node)

| Program | Description | Notes |
|---------|-------------|--------|
| **p51** | Plane/axisymmetric strain | 3/6/10/15-node triangle or 4/8/9-node quad |
| **p55** | Thermal strain (plane/axisymmetric) | 3/6/10/15-node triangle |

**Element:** Right-angled triangles; node count from .dat.

---

## 7. Hexahedra (3D)

| Program | Description | Notes |
|---------|-------------|--------|
| **p53** | 3D elastic solid | 8/14/20-node hexahedron |
| **p56** | 3D elastic solid, no global assembly | 8/14/20-node hexahedron |
| **p57** | 3D hex, PCG, UMAT-style | 3D hex |

**Element:** 3D hexahedron; 8, 14, or 20 nodes (p53, p56).

---

## 8. General / element type in .dat

| Program | Description | Notes |
|---------|-------------|--------|
| **p54** | General 2D/3D elastic | Element type and nod read from .dat |
| **p74** | General 2D/3D steady seepage | Element type and nod from .dat |
| **p88** | General 2D/3D consolidation | Element type and nod from .dat |

**Element:** Defined in .dat (e.g. quadrilateral, triangle, hexahedron, nod).

---

## 9. Stubs or not yet mapped

Programs that are stubs or whose element type was not explicitly confirmed in the scan:

- **p61–p69, p610–p613** – Stubs or bearing capacity / plasticity (element type in full implementation may vary).
- **p94, p95, p96** – Consolidation / Biot (typically 8-node + 4-node pressure or similar; see source for exact element).
- **p52** – Non-axisymmetric axisymmetric (8-node quads).
- **p75** – General 2D/3D seepage, no global assembly (element from geometry).

---

## Quick lookup by element

| Element type | Programs |
|--------------|----------|
| **2-node rod/line** | p41, p42, p71, p81, p82, p83, p93 |
| **2-node beam** | p43, p44, p45, p46, p101, p111 |
| **4-node quad** | p47, p72, p73, p84, p85, p86, p87, p89, p810, p811, p103, p104, p116 |
| **4/8-node quad** | p51, p102, p112 |
| **8-node quad** | p91, p92, p113, p114, p115, p117, p118 |
| **Triangle (3/6/10/15-node)** | p51, p55 |
| **Hexahedron (8/14/20-node)** | p53, p56, p57 |
| **Element from .dat** | p54, p74, p88 |

Use the first token in the .dat (e.g. `p41`, `p42`) to select the program; this table helps you pick the program that matches your element type.
