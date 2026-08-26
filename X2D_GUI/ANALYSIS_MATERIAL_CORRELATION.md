# Correlation: Analysis Type ↔ Failure Criterion ↔ Material Type

How the **Material Properties** tab (Failure Criterion, Material Type) aligns with **Analysis type** and **Element type** in the FE Analysis tab and with the C++ backend (p41–p118).

---

## 1. Material Properties tab (existing)

- **Failure Criterion** (enum): Elastic, Elasto-Plastic, Mohr-Coulomb, Hoek-Brown, Drucker-Prager, Generalized Hoek-Brown  
- **Material Type** (enum): Isotropic, Transversely Isotropic, Orthotropic, Duncan-Chang Hyperbolic, Non-linear Isotropic, Visco-Elastic, Plastic  

The UI already shows **elastic properties** (E, ν) when Elastic + Isotropic; **strength properties** (c, φ, etc.) when another criterion or material type is selected.

---

## 2. Backend support (C++ programs)

| Backend program | Failure / constitutive | Material type in practice | Notes |
|-----------------|------------------------|---------------------------|--------|
| **p41, p42**    | Elastic only          | Isotropic (E, or EA)       | 1D rod / truss |
| **p43, p44, p46** | Elastic only        | Isotropic (E, ν)          | Beam |
| **p45**         | Elasto-plastic (beam) | Plastic hinges            | Beam/frame plasticity |
| **p47**         | Elastic               | Isotropic                 | Plate |
| **p51**         | Elastic (plane strain)| Isotropic (E, ν)          | Triangle/quad, dee matrix |
| **p52–p57**     | Elastic (p53–p56); p57 UMAT-style | Isotropic or user routine | 2D/3D solid |
| **p72–p74**     | N/A (flow)            | Permeability              | Seepage |
| **p81–p88, p93**| Pore pressure / consolidation | Isotropic (k, mv, etc.) | Consolidation |
| **p118**        | Von Mises or Mohr-Coulomb elasto-plastic | Isotropic (E, ν, yield or c, φ, ψ) | 8-node quad |

**Summary:** Most structural/solid programs are **elastic and isotropic**. **p45** (beam plasticity) and **p118** (Von Mises or **Mohr-Coulomb** 2D) implement plasticity. **p118** supports both: 6 props per type → (E, nu, rho, c, phi, psi) for Mohr-Coulomb; (E, nu, rho, yield, 0, 0) for Von Mises. **Transversely isotropic** elasticity is available in the library (`fem::deemat_ti`) for plane strain (3×3 or 4×4); programs such as p51 can be extended to use it when Material Type is Transversely Isotropic. Hoek-Brown and Drucker-Prager are not yet implemented in the C++ solvers; the app can collect parameters for export or future use.

---

## 3. Correlation: Failure Criterion → Analysis type

Use this to **filter or suggest Analysis type** (e.g. in FE Analysis tab) based on **Failure Criterion** chosen in Material Properties, and to show warnings when the combination is unsupported.

| Failure Criterion (Material Properties) | Supported by backend? | Suggested Analysis type (first token) | Notes |
|----------------------------------------|------------------------|----------------------------------------|--------|
| **Elastic**                            | Yes (most programs)    | `rod_1d`, `plane_strain_elastic`, `solid_3d_hex`, `beam_static`, etc. | Use for p41, p42, p43, p44, p46, p47, p51–p57, p101–p104, p111–p117 |
| **Elasto-Plastic**                     | Partial                | `beam_plastic` (p45), `elastoplastic_dynamics` (p118) | p45 = beams; p118 = 2D Von Mises. No general 2D/3D MC or DP in this backend |
| **Mohr-Coulomb**                       | Yes (p118)             | `elastoplastic_dynamics`, `p118`       | p118: 6 props (E, nu, rho, c, phi, psi) per material type; set phi>0 for MC |
| **Hoek-Brown**                         | No                     | —                                      | Same as above |
| **Drucker-Prager**                     | No                     | —                                      | Same as above |
| **Generalized Hoek-Brown**             | No                     | —                                      | Same as above |

**GUI logic suggestion:**

- If **Failure Criterion = Elastic** → allow all analysis types that are elastic (p41–p47, p51–p57, eigenvalue, dynamics with elastic only).
- If **Failure Criterion = Elasto-Plastic** → allow only analysis types that support plasticity: e.g. **Beam elasto-plastic** (p45), **Elasto-plastic forced vibration** (p118). Disable or hide other analysis types, or show a warning.
- If **Failure Criterion = Mohr-Coulomb / Hoek-Brown / Drucker-Prager / Generalized Hoek-Brown** → either:  
  (a) **Warn** that the backend does not implement this criterion and run as Elastic (using E, ν only), or  
  (b) **Disable** “Run FE Analysis” and show a message “Mohr-Coulomb not implemented; use Elastic or Elasto-Plastic (beam/p118)”, or  
  (c) Keep for **export only** (e.g. export to Plaxis/Abaqus).

---

## 4. Correlation: Material Type → Analysis type

Use this to **filter or suggest Material Type** options (or show warnings) based on **Analysis type** and backend capabilities.

| Material Type (Material Properties) | Backend support | Suggested use |
|-------------------------------------|-----------------|----------------|
| **Isotropic**                       | All programs    | Default; use for p41–p118 that take E, ν (or equivalent). |
| **Transversely Isotropic**          | Not in current C++ | Store in app for export; if Run is allowed, treat as Isotropic (e.g. use one E, ν) and warn. |
| **Orthotropic**                     | Not in current C++ | Same as above. |
| **Duncan-Chang Hyperbolic**        | Not in current C++ | Same; or disable Run when selected. |
| **Non-linear Isotropic**           | Not in current C++ | Same. |
| **Visco-Elastic**                   | Not in current C++ | Same. |
| **Plastic**                         | p45, p118       | Allow only when Analysis type is beam plastic or p118; otherwise warn or disable. |

**GUI logic suggestion:**

- When **Analysis type** is set to an elastic program (e.g. plane_strain_elastic, p51):  
  - **Isotropic** → full support.  
  - **Transversely Isotropic / Orthotropic / etc.** → show info: “Backend uses isotropic (E, ν). Anisotropic parameters stored for export only,” and optionally use a single E, ν for the run.
- When **Analysis type** is **Beam elasto-plastic** (p45) or **Elasto-plastic dynamics** (p118):  
  - Allow **Isotropic** and **Plastic** (or Elasto-Plastic failure criterion); other material types can be hidden or warned.

---

## 5. Recommended data flow in the app

1. **Material Properties tab**  
   User sets **Failure Criterion** and **Material Type** per layer (and E, ν, c, φ, etc.). These are the “source of truth” for what the user intends.

2. **FE Analysis tab**  
   User sets **Analysis type** (and optionally **Element type**).  
   - **Option A (analysis-driven):** When user changes Analysis type, the GUI can **warn** if any layer has a Failure Criterion or Material Type not supported by that analysis (e.g. Mohr-Coulomb + plane_strain_elastic).  
   - **Option B (failure-criterion-driven):** When user changes Failure Criterion in Material Properties, the GUI can **filter** the Analysis type list in FE Analysis to only those that support that criterion (e.g. if Elasto-Plastic, show only p45 and p118).

3. **When writing .dat / running**  
   - Map **Analysis type** → first token (e.g. `plane_strain_elastic`, `beam_plastic`).  
   - Map **Failure Criterion** → if Elastic, write only E, ν (and density if needed); if Elasto-Plastic and analysis is p45/p118, write E, ν and plasticity parameters; if Mohr-Coulomb/etc., either do not run or run as Elastic with a clear warning.  
   - Map **Material Type** → if Isotropic, use E, ν; if other, use equivalent isotropic if possible and warn “anisotropic stored for export only.”

4. **Export to other software**  
   Store full Failure Criterion and Material Type (and all strength parameters) so that a future exporter (e.g. to Plaxis, Abaqus) can write the correct model.

---

## 6. Short reference table (for dropdowns / validation)

| Failure Criterion | Material types that make sense | Analysis types (backend) |
|-------------------|--------------------------------|--------------------------|
| Elastic           | Isotropic, (others → treat as isotropic + warn) | p41–p47, p51–p57, p101–p104, p111–p117 |
| Elasto-Plastic    | Isotropic, Plastic             | p45 (beam), p118 (2D Von Mises) |
| Mohr-Coulomb     | Isotropic (c, φ)               | Not implemented; store only or export |
| Hoek-Brown / DP / GHB | Isotropic (params)         | Not implemented; store only or export |

This table can drive which **Analysis type** options are enabled in the FE Analysis tab and which **Failure Criterion** / **Material Type** combinations trigger a warning or disable Run.
