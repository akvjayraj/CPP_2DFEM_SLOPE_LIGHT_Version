# Boundaries

Geometry Definition is where you build the 2D soil domain: points, segments (lines and arcs), circles, and closed surfaces that become material regions.

## Building blocks

| Entity | Role |
|--------|------|
| **Points** | Vertices of the model |
| **Lines** | Straight boundary segments |
| **Arcs** | Curved segments with arc metadata |
| **Circles** | Full circles (including tunnel and internal circles) |
| **Surfaces / regions** | Closed polygons formed from selected segments |

## Closed soil boundary

FE workflows expect a **closed outer soil boundary** that encloses the slope and foundation materials. Internal polylines divide layers and lenses inside that outer loop.

Recommended practice:

1. Digitize the ground surface and foundation extents as a single closed outer polygon — **or** import borehole logs on **Analysis → Field Stations** and click **Create soil block** (a closed rectangle around the stations plus **Block margin**). See [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md).
2. Add layer interfaces as internal edges that connect cleanly to the outer boundary or to other interfaces. From Field Stations, **Commit interfaces** writes horizontal cuts; **Draw non-horizontal layer…** snaps to borehole contacts.
3. Create surfaces/regions from the closed loops. **Add Surface** preselects the outer-ring line segments (`L1…Ln`) in the Surface grid when a closed boundary already exists; click Action **Add** to lock the region (you can still toggle picks on the canvas first).
4. If you started from a soil-block rectangle, the empty area above the borehole pins is **padding**, not the ground surface. Each pin is the **collar** (where that hole was drilled). Cut the slope so the ground line passes through the collars — edit vertices, Draw Lines, or Surface **Excavate** then **Geometry → Commit Kernel Booleans**. After that cut, the box top is no longer sitting in “air” above the holes. **Plot boreholes** is an evidence overlay only; it does not change the outer boundary.
5. Run **Geometry → Verify Geometry** before assigning materials or analysing.

> **Warning:** Open polygons, crossing edges, or unassigned regions commonly cause empty meshes or invalid elements. Fix verification issues before meshing or running FE analysis.

## Editing and stages

Geometry commands under the Geometry menu include adding surfaces/regions, clearing stage geometry when staging is active, verifying geometry, heal, geological polylines (weak plane / bedding / tension crack), kernel boolean commit, and multi-format import/export. Construction stages can hold excavation polygons that modify which material remains active.

**Verify Geometry** checks more than the outer ring: closed faces, dangling internals, overlapping materials, tunnels outside the soil, and incomplete geological features. See [Production geometry and interchange](production-geometry.md).

## Display

Axis limits and grid spacing from Project Settings → Grid & Axes control the drawing window. Use View toggles for axes/grid visibility while digitising. Snap behaviour is configured under **Geometry → Snap Settings…**.

## See also

- [Excavations and tunnels](excavation-tunnel.md)
- [Importing geometry](import.md)
- [Production geometry and interchange](production-geometry.md)
- [Layers and assignment](../materials/layer-assignment.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
- [Cut the slope — Method A](../tutorials/field-stations-slope-method-a.md)
- [Cut the slope — Method B](../tutorials/field-stations-slope-method-b.md)
- [Cut the slope — Method C](../tutorials/field-stations-slope-method-c.md)
- [Grid and axes](../project-settings/grid-axes.md)
