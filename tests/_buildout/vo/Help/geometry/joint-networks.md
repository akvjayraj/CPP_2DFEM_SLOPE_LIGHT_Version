# Joint networks

Use **Geometry → Joint Wizard…** and choose **Joint network** to generate a discrete fracture pattern inside selected model region(s). Multiple named networks can exist on a project, and each network can target a different set of surfaces.

Select the **Joint Model** first. It gates every other parameter, matching the RS2 joint-network workflow.

## Regions

Under **Apply to regions**, tick one or more `GeometrySurface` rows (`S1 … Sn`, name and material). At least one region is required. Generated traces are clipped to the union of the selected polygons. OK replaces only traces that belong to the active network id; other networks and hand-drawn joints stay.

## Joint models

| Model | What it generates | Principal parameters |
|-------|-------------------|----------------------|
| **Parallel Deterministic** | Parallel planes at fixed inclination and spacing | Inclination from +X, spacing, infinite length or length + persistence (0–1), random or through-point location, optional offset |
| **Parallel Statistical** | Parallel planes with statistical spacing / length / persistence | Inclination (constant), spacing distribution, optional finite length and persistence distributions, seed. Auto min/max uses 3× standard deviation |
| **Cross Jointed** | Continuous bedding plus cross joints that form blocks | Bedding and cross inclinations (constant; default cross = bedding + 90°), spacing distributions. Cross spacing is measured *along* the bedding |
| **Baecher** | Independent finite traces (Poisson centers) | Intensity P21 (traces / area), orientation and length distributions, seed. Joints generally terminate in intact rock |
| **Veneziano** | Finite segments along Poisson lines (coplanar traces) | Line intensity, orientation, length, persistence, seed |
| **Voronoi** | Cell edges of a 2D tessellation | Density (cells / area), site count, or average joint length; regularity (Irregular / Medium Regular / Almost Regular / Regular Hexagon); optional anisotropy; seed |

A status warning appears if generation hits the 2000-trace cap. Reduce density, intensity, or spacing and click **Randomize** (new seed) to preview again.

## Mesh, then Run FE

Joint traces (hand-drawn or generated networks) are **conforming mesh constraints**. Click **Mesh** so the traces become triangle/quad edges; the mesher then splits nodes along each joint so the two sides can slip and emits 4-node Goodman interface elements.

**Run FE** writes those interfaces into the `.dat` Joints block (`ninterface` + `interfaces` table). They assemble in:

- **p610 T3 elastoplastic** — kn/ks stiffness and Mohr–Coulomb slip in the viscoplastic loop
- **p64 Q8 SRF** — same interface elements; joint c/φ are **not** reduced with SRF (intact rock is)

Closed ends stay shared (no slip at that tip). Open ends are split. Q8 joints use two linear 4-node pieces (corner–midside and midside–corner).

## Properties and export

The wizard **Joint Property**, end condition, and install stage apply to every generated trace. Traces are stored as geological features tagged with the network id and export through the `.dat` Joints block (geometry comments plus the numeric interfaces table after meshing). Regenerating a network deletes only that network’s traces.

## See also

- [Boundaries](boundaries.md)
- [Production geometry and interchange](production-geometry.md)
- [RS2: Application of Joint Networks](https://www.rocscience.com/help/rs2/tutorials/jointed-rock/application-of-joint-networks)
