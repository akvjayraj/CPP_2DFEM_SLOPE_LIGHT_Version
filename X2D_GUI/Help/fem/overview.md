# Finite element analysis

Geotechnical Slope Stability (FEM build) is driven from the **Mesh**, **Boundary Conditions**, **FE Analysis**, and **Script Editor** tabs, with analysis categories selected under **Project Settings → Analysis**.

> **Note:** This chapter is a short overview. Detailed meshing, staged construction and solver options will be expanded in a later edition of the manual. For a first run, start with the [FEM quick start](../getting-started/quick-start-fem.md).

## What the FE path is for

Use FE analysis when you need:

- Strength-reduction (SRF) factors of safety on a continuum mesh
- Seepage heads or transient dissipation of excess pore pressure
- Staged excavation / construction sequences
- Continuum displacement, stress or yield fields rather than a single slip surface

## Main entry points

| UI | Role |
|----|------|
| **Mesh** | Discretize the closed soil domain |
| **Loads / Boundary conditions** | Gravity, surcharges, and restraints |
| **FE Analysis** | Run SRF, seepage, or related continuum analyses |
| **Project Settings → Analysis** | Turn categories ON and pick a tree leaf (structural, SRF, Flow-Seepage Static / Steady / Transient) |
| **Script Editor** | Edit and run solver `.dat` decks (p41, p51, p610 family, `fem_cpp`) |
| **Verification** | Compare selected FE benchmarks against analytical targets |
| **Results** | Contours, vectors, and export (CSV / VTK) |

## Typical FE workflow

1. Build geometry and materials (or import Field Stations and cut the slope).
2. Generate a mesh on the Mesh tab.
3. Apply loads and boundary conditions; enable groundwater when needed.
4. Under **Project Settings → Analysis**, enable the FE category you need.
5. Run the solver from **FE Analysis** or the [Script Editor](script-editor.md).
6. Review output under [FE results](fe-results.md).

Pore pressures from a **Steady State** seepage run can be transferred one-way into a strength-reduction run when SRF is also ON. Transient State is θ-method dissipation, not two-way solid–fluid coupling.

## See also

- [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md)
- [Script Editor](script-editor.md)
- [FE results](fe-results.md)
- [Verification tab](verification-tab.md)
- [Analysis type](../project-settings/analysis-type.md)
