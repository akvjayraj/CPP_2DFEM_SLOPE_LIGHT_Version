# Using analysis type and element type like professional FEM software

How the concepts of **analysis type** and **element type** in this codebase (and in the `.dat` format) align with professional tools such as **Rocscience** (RS2, RS3, Slide), **Abaqus**, and **Plaxis**, and how to use them in a professional-style workflow.

---

## How professional software exposes these concepts

### 1. **Analysis type (physics)**

In commercial packages, you typically choose **what kind of problem** you are solving *before* or *with* the mesh:

| Software      | Where it appears | Typical options |
|---------------|------------------|------------------|
| **Rocscience RS2/RS3** | Analysis type / analysis settings | Elastic, Consolidation, Slope stability, Groundwater, Dynamic |
| **Plaxis 2D/3D**      | Calculation type / phase type     | Plastic, Consolidation, Safety, Groundwater flow, Dynamics |
| **Abaqus**            | Step type (Analysis step)         | Static, Dynamic, Heat transfer, Soils (consolidation), Coupled |
| **This codebase**      | First token in `.dat`             | `rod_1d`, `plane_strain_elastic`, `consolidation_4node`, `seepage_2d`, etc. |

So **“analysis type”** = **physics** (static, seepage, consolidation, dynamics, etc.). Your `.dat` first token is exactly that: it selects the analysis (and thus the solver program).

### 2. **Element type (mesh)**

Then you choose **how the domain is discretized**:

| Software      | Where it appears | Typical options |
|---------------|------------------|------------------|
| **Rocscience** | Mesh settings / element type | 3-node triangle, 6-node triangle, 4-node quad, 8-node quad |
| **Plaxis**    | Element type per cluster / mesh | 6-node triangle, 15-node triangle, 8-node quad |
| **Abaqus**    | Element family + type (e.g. CPE4, C3D8) | Beam, Shell, Solid (tet/hex), Pipe, etc. |
| **This codebase** | Implied by program or optional 2nd token | 2-node rod/line, 2-node beam, 4-node quad, 8-node quad, hex; see `PXX_BY_ELEMENT_TYPE.md` |

So **“element type”** = **element family + nodes** (rod, beam, 4-node quad, 8-node quad, hex, etc.). In our `.dat` you can optionally write an element token (e.g. `rod_2node`, `line_2node`) for clarity; the program is already determined by the analysis type.

### 3. **Typical workflow in professional software**

1. **Create geometry** (points, lines, surfaces, volumes).
2. **Assign materials** (and optionally regions).
3. **Choose analysis type** (e.g. “Elastic”, “Consolidation”, “Seepage”).
4. **Define mesh / element type** (e.g. “6-node triangles”, “8-node quads”).
5. **Apply BCs and loads.**
6. **Run** → results.

In our backend the same idea is: **analysis type (first token) + optional element token + rest of .dat**. The GUI (e.g. X2D_GUI) can mirror this with an “Analysis type” dropdown and an “Element type” dropdown, then write the correct `.dat` for `fem_cpp`.

---

## Mapping to Rocscience, Abaqus, Plaxis

### Rocscience (RS2, RS3, Slide)

- **Analysis type** in RS2/RS3: e.g. *Elastic*, *Barton–Bandis*, *Mohr–Coulomb*, *Consolidation*, *Groundwater*, *Dynamic*.  
  → Map to our first token: `plane_strain_elastic`, `consolidation_4node`, `seepage_2d`, `forced_vibration_8node`, etc.
- **Element type**: Usually 3- or 6-node triangles, or 4-node quads (RS2).  
  → Our side: p51 (tri/quad), p72/p85 (4-node quads for seepage/consolidation). Use `ANALYSIS_TYPES.md` and `PXX_BY_ELEMENT_TYPE.md` to pick the right program and token.
- **Use in a “professional” way**: In your GUI, offer an “Analysis type” list (e.g. “Plane strain – elastic”, “Steady seepage”, “Consolidation”) and optionally “Element type” (e.g. “4-node quad”, “8-node quad”). When exporting or running, set the first token (and optional second) in the `.dat` from that choice and call `fem_cpp` with the dataname.

### Abaqus

- **Analysis type** = **Step** type: *Static*, *Dynamic*, *Heat transfer*, *Soils* (consolidation), *Coupled temp-displacement*, etc.  
  → Map to our: `plane_strain_elastic`, `solid_3d_hex`, `thermal_conduction`, `consolidation_4node`, `forced_vibration_8node`, etc.
- **Element type** = **Element library** (e.g. CPE4, C3D8, B21, S4).  
  → Our: 2-node rod (B21-like) → p41/p42; 2-node beam → p43–p46, p101, p111; 4-node plane strain → p51, p103, p116; 8-node → p102, p112, p117; 3D hex → p53, p56, p57.
- **Use in a “professional” way**: When you export from Abaqus (or from your own GUI) to our format, define a mapping: Abaqus step type + element family → our analysis token; then write the rest of the `.dat` (mesh, materials, BCs) in our order. Your “Analysis type” in the GUI can mimic Abaqus step names and map to our tokens under the hood.

### Plaxis (2D/3D)

- **Analysis type** = **Calculation type** (e.g. *Plastic*, *Consolidation*, *Safety*, *Groundwater*, *Dynamic*).  
  → Map to our: `plane_strain_elastic`, `consolidation_4node`/`consolidation_4node_2`, `seepage_2d`, `forced_vibration_8node`, etc.
- **Element type**: 6-node triangles, 15-node triangles, 8-node quads (and 3D equivalents).  
  → Our: p51 (tri/quad), p72/p85 (4-node quads), p102/p112/p117 (4/8-node quads). See `PXX_BY_ELEMENT_TYPE.md` for which program uses which element.
- **Use in a “professional” way**: Same as above: “Calculation type” dropdown → our analysis token; “Element type” or mesh choice → optional element token and correct program selection; write `.dat` and run `fem_cpp`.

---

## Using this in your own professional-style software (e.g. X2D_GUI)

### 1. **UI: Analysis type + Element type**

- **Analysis type** (required): dropdown or list from `ANALYSIS_TYPES.md` (e.g. “1D axial rod”, “Plane strain – elastic”, “Steady seepage 2D”, “Consolidation 4-node”, “Forced vibration 8-node”).
- **Element type** (optional but recommended): dropdown from `PXX_BY_ELEMENT_TYPE.md` (e.g. “2-node rod”, “2-node beam”, “4-node quad”, “8-node quad”).  
  Constrain choices so that only element types valid for the selected analysis type are shown (e.g. for “Plane strain – elastic” allow triangle/quad; for “1D axial rod” only “2-node rod”).

### 2. **Export / run**

- Map **(Analysis type, Element type)** → **first token** (and optional second token) for the `.dat`:
  - First token: from `ANALYSIS_TYPES.md` or `common.cpp` `resolve_analysis_type()` (e.g. `rod_1d`, `plane_strain_elastic`, `seepage_2d`, `consolidation_4node`).
  - Optional second token: from `PXX_BY_ELEMENT_TYPE.md` (e.g. `rod_2node`, `line_2node`, “quad_4node”, “quad_8node”) if the target program supports it (e.g. run_p41).
- Write the rest of the `.dat` in the order expected by that program (mesh, materials, BCs, loads, etc.), then run:
  - `fem_cpp.exe <dataname>`
  or your existing runner (e.g. `CppAnalysisRunner` with the same dataname).

### 3. **Consistency with Rocscience/Abaqus/Plaxis**

- **Analysis type** in your UI = same idea as “Analysis” in Rocscience, “Step type” in Abaqus, “Calculation type” in Plaxis.
- **Element type** in your UI = same idea as “Element type” / “Mesh type” in those packages.
- The `.dat` first (and optional second) token is the machine-readable form of that choice; the rest of the file is the usual FEM data (geometry, mesh, materials, BCs, loads) in the order our programs expect.

This way, your software can be used in a “professional” way (choose analysis and element type, then run) while still driving the same `fem_cpp` backend and `.dat` format under the hood.
