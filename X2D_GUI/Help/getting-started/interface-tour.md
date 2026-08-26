# Tour of the interface

The main window centres on a tab strip for the modelling workflow, with menus and a toolbar above and stage / status controls where staging is active.

## Main tabs

Tabs are ordered left to right as follows:

| # | Tab | Role |
|---|-----|------|
| 0 | Project Settings | Metadata, units, grid/axes, analysis type (including Field Stations) |
| 1 | Geometry Definition | Boundaries, surfaces, excavations, tunnels, geological features, import |
| 2 | Material Properties | Material library and region assignment |
| 3 | Mesh | Discretize the soil domain (triangles / quads) |
| 4 | Load Conditions | Gravity, point / line / pressure loads, seismic |
| 5 | Boundary conditions | Restraints and prescribed values on the mesh / outline |
| 6 | Groundwater | Water tables and seepage boundary conditions |
| 7 | FE Analysis | Strength reduction, seepage, and solver run controls |
| 8 | Verification | Built-in verification problems and comparisons |
| 9 | Results Visualization | Displacement, stress, yield, and pore-pressure plots |
| 10 | Sensitivity, Probabilistic & Back Analysis | SRF tornado, sampling, back analysis, and named analysis cases |
| 11 | Report Generation | Letterhead, Markdown preview, and PDF export |
| 12 | Script Editor | Solver script (`.dat`) editing and related automation |

Most modelling tabs stay disabled until a project is open.

## Menus

| Menu | Typical use |
|------|-------------|
| **File** | New / Open / Save / Close project; import and export geometry, mesh, materials, results, reports |
| **View** | Display toggles (grid, axes, node/element numbers) |
| **Edit** | Editing commands for the active context |
| **Geometry** | Navigate to Geometry Definition; surface/region tools; verify; heal; weak plane / bedding / tension crack; kernel booleans; measure; import/export |
| **Material Properties** | Add, edit, assign, and library import/export |
| **Loading** | Navigate to Load Conditions; add or clear loads |
| **Analysis** | Run / stop FE analysis; jump to Mesh, BC, Groundwater, Verification |
| **Results** | Results Visualization, export, Report Generation |
| **Window** | Jump to any main tab by name |
| **Help** | View Help (F1), Getting Started, User Manual, Tutorials |

## Help and navigation

- **F1** or **Help → View Help** — context help for the selected tab.
- **Help → User Manual...** — full manual with contents tree and search.
- **Help → Tutorials...** — illustrated walkthroughs, including [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md).
- **Help → Getting Started...** — [Quick start: your first FEM analysis](quick-start-fem.md).
- **Window** menu — jump directly to Project Settings, Geometry Definition, Material Properties, and other tabs.

## See also

- [Overview](overview.md)
- [Projects and files](project-files.md)
- [Quick start: your first FEM analysis](quick-start-fem.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
