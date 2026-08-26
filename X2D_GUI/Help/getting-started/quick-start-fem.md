# Quick start: your first FEM analysis

This walkthrough builds a simple 2D slope, meshes it, and runs a finite-element analysis (strength reduction and/or seepage).

> **Tip:** To build layers from borehole logs instead of drawing the outline by hand, open **Help → Tutorials...** → [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md).

## 1. Create a project

1. Choose **File → New Project**.
2. Open **Project Settings**.
3. On **General**, set Project Title, Author, and Unit System (SI or Imperial).
4. On **Grid & Axes**, set axis limits that comfortably enclose the slope and choose a grid spacing.
5. On **Analysis Type**, confirm a **2D Problem**.

## 2. Draw or import geometry

1. Open **Geometry Definition**.
2. Create a **closed soil boundary** (outer polygon) that defines the ground profile and foundation extent — **or** import borehole logs on **Analysis → Field Stations** and **Create soil block**, then commit or draw layers and cut the slope (see the [Field Stations tutorial](../tutorials/field-stations-import.md)).
3. Add internal layer lines or surfaces if the slope has more than one material.
4. Use **Geometry → Verify Geometry** to catch open polygons, dangling edges, or invalid regions before meshing.

> **Tip:** Import is available from **File → Import → Geometry (CSV / DXF / LandXML / GeoJSON)…** (`Ctrl+Shift+G`). After CAD import, **Geometry → Heal Geometry** can close small gaps. A photo or scanned drawing can be placed with **File → Import → Slope Image…**. See [Production geometry](../geometry/production-geometry.md).

## 3. Define and assign materials

1. Open **Material Properties**.
2. Add a material (unit weight, stiffness, and a strength model such as Mohr–Coulomb).
3. Assign the material to each soil region on the canvas or through the assignment commands.

## 4. Generate the mesh

1. Open the **Mesh** tab.
2. Choose an element family (for example T3 / T6 triangles or Q4 / Q8 quads) and a target size.
3. Discretize the domain. Confirm nodes and elements cover every assigned region.

> **Warning:** A valid closed outer boundary and assigned materials are required before meshing can produce a usable continuum.

## 5. Loads and boundary conditions

1. On **Load Conditions**, add gravity (and optional surcharges or seismic coefficients).
2. On **Boundary Conditions**, restrain the far-field and base (typical: fix X on the sides, fix X and Y on the base).
3. Optionally import outer-boundary restraints from the geometry outline.

## 6. Groundwater (optional)

- On **Groundwater**, enable a water table or seepage boundary conditions when pore pressures matter.
- Seepage results can feed later strength-reduction or coupled runs. See [Groundwater overview](../water/groundwater-overview.md).

## 7. Run FE Analysis

1. Open **FE Analysis**.
2. Choose the analysis type (for example strength-reduction / SRF, seepage, or a Script Editor deck).
3. Run the solver (**Analysis → Run FE Analysis** or **F5**).
4. Watch the output pane for convergence and the critical SRF when applicable.

## 8. Review results

1. Open **Results Visualization** to inspect displacements, stresses, yield, and pore pressures.
2. Use **Verification** for built-in benchmark comparisons, and **Report Generation** to export a summary.
3. Save the project (**File → Save**).

## See also

- [Boundaries](../geometry/boundaries.md)
- [Defining materials](../materials/defining-materials.md)
- [Finite element analysis](../fem/overview.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
- [Groundwater overview](../water/groundwater-overview.md)
