# `.xfem` project file format

Native GeotechSlopeApp project files are **UTF-8 JSON** with extension **`.xfem`**.

- **formatVersion**: `"1.3"` (current), `"1.2"`, `"1.1"`, or `"1.0"` (load supported for all four)
- **main**: Selected main tab index, zoom/pan, current stage
- **projectSettings**: General / analysis / grid, stage footer tabs, axis limits
- **geometry**: Vertices, segments (including arc metadata), surfaces (picked lines, locked/draft polygons, assigned material id), internal edges, circles, tunnels, groundwater lines, excavations, boolean surface ops, layer assignments, surface material paint fills
- **materials**: Failure criterion, material type, analysis routing, full material library
- **mesh**: Discretized nodes/elements, mesh information rows, mesh popup UI (RS2D, density, tunnel params, selected popup tab)
- **userDefinedMesh** (1.2+): User-Defined Mesh node/element tables, element type, Q8 embankment/rectangular parameters
- **loads**: All load rows
- **fieldStress**: Global/overrides/traction groups (Load Conditions)
- **boundaryConditions**: BC rows and boundary-canvas UI strings
- **feAnalysis**: Solver options, staged analysis grid, SRF trial F-values (1.2+), FE Analysis subtab index (1.2+). Each stage row may include **pseudoStaticKh** / **pseudoStaticKv** for Seismic / Dynamic stages. Script `.dat` mirrors these as `! k_h` / `! k_v` in `! <BEGIN:AnalysisOptions>`; `fem_cpp` JSON may export `pseudo_static` and `pseudo_static_nodal_forces` for Results visualization.
- **stagedConstruction**: Per-stage excavations (polygons), stage names/types, excavated element IDs, active stage
- **results**: Visualization options, FE run snapshot with nodes/elements/stresses (1.2+), Results subtab index (1.2+)
- **verification** (1.2+): Selected problem and verification input fields
- **script**: Script editor content/output, manual-content flag

**File → Save** writes to the last **Save As** / **Open** path; if none, **Save** opens **Save As**. Before save, SRF projects sync the Script Editor `.dat` from the current mesh on the Mesh tab. When the Material Properties tab is active, geometry edits on that tab are merged into the canonical geometry snapshot first (1.3+).

**File → Save As** and **Save a Copy** use the same full snapshot.

Future versions may add fields; unknown JSON properties are ignored on deserialize.
