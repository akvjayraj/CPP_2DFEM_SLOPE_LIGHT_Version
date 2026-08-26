# Layers and assignment

Soil layers are closed geometric regions. Material assignment links each region to one material from the library so unit weight and strength are applied correctly along slip surfaces.

## Assigning materials

1. Create closed surfaces/regions on **Geometry Definition**.
2. Open **Material Properties**.
3. Use **Assign Material to Region...** (or the tab’s assign-to-region workflow) to paint or pick regions.
4. Confirm colours on the canvas match the intended materials.
5. Use **Clear All Material Assignments** only when you intend to reset the model.

Menu path: **Material Properties → Assign Material to Region...**.

## Layer structure tips

- The outer closed soil boundary should contain all layers. **Create soil block** on Analysis → Field Stations builds that rectangle from borehole logs when Geometry is empty (or on demand).
- Internal interfaces should connect so every soil polygon is closed.
- **Horizontal layers** from Field Stations: **Commit interfaces** writes full-width cuts at unit contact elevations (Layer 1 = bottom).
- **Non-horizontal layers:** **Draw non-horizontal layer…** on Field Stations, snap through borehole contacts, then **Add Layer** on Material Properties → Layering (Non-Horizontal). Commit does not auto-connect stations into sloping polylines.
- Bedrock or free-draining hard layers can be marked **impenetrable** on the material.
- Strength anisotropy (if used) is defined on the material, not by forcing horizontal strata.

> **Warning:** Regions without a material assignment do not contribute a coherent strength envelope. Verify assignments before searching slip surfaces.

## Staging

When construction stages excavate material, only the remaining assigned regions participate in that stage. Re-check assignments after major excavation or boolean edits.

## See also

- [Boundaries](../geometry/boundaries.md)
- [Defining materials](defining-materials.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
