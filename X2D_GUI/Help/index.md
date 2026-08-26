# Welcome

Welcome to the **Geotechnical Slope Stability** user manual for GeotechSlopeApp (finite-element / FEM build). This guide covers building a 2D slope model, assigning materials, generating a mesh, applying loads and boundary conditions, running strength-reduction (SRF) and seepage analyses, and reviewing continuum results.

Wave 1 geometry tools — heal, kernel booleans, drawable weak planes / bedding / tension cracks, slope-image underlays, and CSV / DXF / LandXML / GeoJSON interchange — are included so CAD and borehole-log models can be cleaned before meshing.

## How to open Help

| Action | Result |
|--------|--------|
| **Help → User Manual...** | Opens this manual at the home page |
| **Help → Tutorials...** | Opens illustrated step-by-step walkthroughs |
| **Help → View Help** or **F1** | Opens context help for the active main-window tab |
| **Contents** (help toolbar) | Returns to this welcome page |
| Back / Forward | Walks the topic history in the help browser |

## Using the help browser

The help window has three areas:

1. **Contents tree** (left) — chapters and topics from the installed manual. Expand a chapter and select a topic to read it.
2. **Search** — type at least two characters to filter topics by title and body text. Clear search to restore the full contents tree.
3. **Topic pane** (right) — rendered markdown for the selected topic, with a breadcrumb above the page.

> **Note:** F1 maps to the main tab that currently has focus (for example, Project Settings, Geometry Definition, Mesh, or FE Analysis). Topics that document a tab are linked through the help table of contents.

## Major chapters

| Chapter | Start here |
|---------|------------|
| Getting started | [Overview](getting-started/overview.md) · [Interface tour](getting-started/interface-tour.md) · [Projects and files](getting-started/project-files.md) · [Quick start (FEM)](getting-started/quick-start-fem.md) |
| Tutorials | [Overview](tutorials/overview.md) · [Field Stations — soil block](tutorials/field-stations-import.md) · [Slope Method A](tutorials/field-stations-slope-method-a.md) · [B](tutorials/field-stations-slope-method-b.md) · [C](tutorials/field-stations-slope-method-c.md) |
| Project Settings | [General](project-settings/general.md) · [Grid and axes](project-settings/grid-axes.md) · [Analysis type](project-settings/analysis-type.md) |
| Geometry | [Boundaries](geometry/boundaries.md) · [Excavations and tunnels](geometry/excavation-tunnel.md) · [Import](geometry/import.md) · [Slope image](geometry/slope-image.md) · [Production geometry](geometry/production-geometry.md) |
| Materials | [Defining materials](materials/defining-materials.md) · [Layers](materials/layer-assignment.md) |
| Groundwater | [Overview](water/groundwater-overview.md) · [Water table](water/water-table.md) |
| Finite element | [Overview](fem/overview.md) · [Script Editor](fem/script-editor.md) · [FE results](fem/fe-results.md) · [Verification](fem/verification-tab.md) |
| Delivery | [Sensitivity, probabilistic, reports, and export](analysis/wave3-delivery.md) |
| Reference | [`.xfem` format](reference/xfem-format.md) · [Keyboard shortcuts](reference/keyboard-shortcuts.md) · [Glossary](reference/glossary.md) |

## Typical FEM workflow

1. Set project metadata and units under Project Settings.
2. Draw or import a closed soil boundary, **or** import borehole logs on **Analysis → Field Stations** and **Create soil block** (then commit horizontal layers or draw non-horizontal interfaces, and cut the slope with Geometry Definition). Heal and verify topology if the drawing came from CAD.
3. Define materials and assign them to regions.
4. Generate a mesh on the Mesh tab.
5. Apply loads and boundary conditions; enable groundwater / seepage when needed.
6. Run the analysis from **FE Analysis** (SRF, seepage, or the Script Editor solver path).
7. Review displacements, stresses, yield, and pore pressures on the Results tab.

See also: [Quick start: your first FEM analysis](getting-started/quick-start-fem.md) · [Field Stations — soil block](tutorials/field-stations-import.md) · [Slope Method A](tutorials/field-stations-slope-method-a.md) / [B](tutorials/field-stations-slope-method-b.md) / [C](tutorials/field-stations-slope-method-c.md).
