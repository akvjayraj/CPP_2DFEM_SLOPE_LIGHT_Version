# Getting started

GeotechSlopeApp is a desktop application for **2D geotechnical slope stability**. You build a geometric model, assign materials and optional groundwater and loads, generate a mesh, apply boundary conditions, then evaluate the slope with finite-element (FE) methods — including strength-reduction (SRF) factors of safety and seepage.

## What you can analyse

- **Continuum FE** — displacements, stresses, yield, and strength-reduction factors of safety on a 2D mesh (T3 / T6 / Q4 / Q8 and related solvers).
- **Seepage** — water tables and flow boundary conditions; pore pressures can feed later SRF or coupled runs.
- **Production geometry** — heal, kernel booleans, drawable weak planes / bedding / tension cracks, and CSV / DXF / LandXML / GeoJSON interchange (see [Production geometry](../geometry/production-geometry.md)).
- **Field Stations** — borehole-log evidence packs that create a closed soil block you then cut into a slope.
- **Staged excavation / tunnels** — openings and construction stages that remain meshable after boolean commit.
- **Verification** — built-in benchmark problems compared against analytical or published targets.

## Recommended first path

1. Read [Tour of the interface](interface-tour.md) to learn the tab and menu layout.
2. Understand [Projects and files](project-files.md) (`.xfem` JSON projects).
3. Follow the text [Quick start (FEM)](quick-start-fem.md) (**Help → Getting Started...**). To build layers from borehole logs, use [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md).

## Units and storage

The Unit System on Project Settings → General is **SI** or **Imperial**. The model stores SI internally; the interface converts displayed quantities when Imperial is selected. Projects save as UTF-8 JSON with the `.xfem` extension.

> **Note:** Context help (F1) opens the topic associated with the active main tab. Use **Help → User Manual...** when you want the full contents tree.

## See also

- [Interface tour](interface-tour.md)
- [Project Settings — General](../project-settings/general.md)
- [Analysis type](../project-settings/analysis-type.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
