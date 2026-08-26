# Excavations and tunnels

Geometry Definition supports excavation polygons and tunnel helpers so staged or underground openings can be represented in the same project as the soil boundary.

## Excavations

Excavation operations remove soil from the active geometry for a construction stage:

- Digitize excavation polygons against the existing surfaces.
- Associate excavations with stage definitions when staged construction is enabled.
- Clear stage geometry from the Geometry menu when you need to reset a stage’s excavation set.

Excavated regions no longer contribute material weight or strength for that stage’s FE evaluation.

> **Note:** Keep excavation boundaries consistent with the outer soil boundary so remaining regions stay closed and meshable.

## Tunnel wizard

Tunnel tools create circular or polygonal openings using tunnel excavation parameters (circle rows, polygon outlines, and related mesh-generation options when FE meshing is used). Typical steps:

1. Choose the tunnel shape parameters (centre, radius, or polygon).
2. Generate the tunnel opening inside the soil domain.
3. Assign lining or surrounding materials as needed on Material Properties.
4. Verify geometry after the boolean cut.

Tunnel mesh generation methods include options such as CDT, ring refinement, AFM, and smoothing. After the boolean cut, assign lining or surrounding materials and verify that remaining regions stay closed and meshable.

## Boolean surface operations

Surface boolean operations (Union / Subtract / Intersect) combine or subtract closed shapes when building complex openings. Fill can create new soil regions.

To make fill/cut part of the solid planar kernel (recomputable history, not only a preview overlay):

1. Complete the boolean / fill operations on the Geometry tab.
2. Choose **Geometry → Commit Kernel Booleans**.
3. Run **Geometry → Verify Geometry** and re-check material assignment.

Details: [Production geometry and interchange](production-geometry.md).

## See also

- [Boundaries](boundaries.md)
- [Importing geometry](import.md)
- [Production geometry and interchange](production-geometry.md)
- [Defining materials](../materials/defining-materials.md)
