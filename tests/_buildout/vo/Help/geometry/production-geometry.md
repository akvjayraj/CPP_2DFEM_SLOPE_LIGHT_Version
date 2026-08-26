# Production geometry and interchange

This topic covers CAD-grade topology, geological constraints, solid fill/cut commits, and multi-format CAD/GIS interchange. Use it when survey drawings, weak planes, or round-trip export matter—not only hand-digitised vertices.

## Planar geometry kernel

The application rebuilds a **planar map** (vertices, edges with roles, faces/regions) from the editable Geometry Definition collections. The kernel supports verification, true boolean commits, geological features, and file export.

| Edge role (conceptual) | Typical use |
|------------------------|-------------|
| Boundary | Outer soil ring |
| Material contact | Layer / region interfaces |
| Excavation | Cut voids from boolean subtract |
| Weak plane / Bedding / Tension crack | Geological constraint polylines |
| Water table | Reserved for groundwater mapping on import |

### Geometry menu commands

| Command | Action |
|---------|--------|
| **Geometry → Heal Geometry** | Merge near-coincident vertices within tolerance (close small gaps after import) |
| **Geometry → Commit Kernel Booleans** | Replay excavate / fill history into solid kernel faces (recomputable) |
| **Geometry → Verify Geometry** | Topology report: outer ring, faces, dangling edges, material overlaps, tunnels, geological features |
| **Geometry → Add Joint…** | Place joints at crest–toe, layer contacts, tension crack, or a dip through a point; pick a property set, end condition, and install stage |
| **Geometry → Joint Properties…** | Define reusable Mohr–Coulomb joint sets (peak/residual strength, kn/ks, pore pressure, staged factors) |
| **Geometry → Draw Weak Plane…** | Start a weak-plane polyline |
| **Geometry → Draw Bedding / Contact…** | Start a bedding or contact polyline |
| **Geometry → Draw Tension Crack…** | Start a tension-crack polyline |
| **Geometry → Finish Geological Feature** | Commit the polyline being drawn (needs ≥2 vertices) |
| **Geometry → Cancel Geological Feature** | Discard the draft polyline |

> **Note:** Verify Geometry runs automatically after a successful geometry import. Fix **Error** severity items before meshing or **Run FE Analysis**.

## Geological features

Drawable constraints are stored in the `.xfem` project and appear on the Geometry canvas. In the FEM build they document weak planes, bedding, and tension cracks for model review and interchange (DXF / GeoJSON layers).

| Feature | Purpose |
|---------|---------|
| **Weak plane** | Preferential discontinuity polyline |
| **Bedding / contact** | Orientation / contact polyline |
| **Tension crack** | User-drawn crack geometry (optional water elevation on the feature) |

### Joint properties and Add Joint

Use **Geometry → Joint Properties…** to maintain a reusable library of Mohr–Coulomb joint sets (peak and optional residual strength, kn/ks in kPa/m, groundwater pore pressure, additional pressure, and staged factors). **Geometry → Add Joint…** (or Boundaries → Add Joint) places an instance: choose a property set, end condition (open ends where the joint meets a free surface or another joint), and install stage. Suggested-location chips fill crest–toe, each material-layer contact, or a crest tension crack. Through-point and parallel-set modes clip a dip line to the domain. **Joint network** generates Parallel Deterministic / Statistical, Cross Jointed, Baecher, Veneziano, or Voronoi traces in selected regions (multiple named networks; OK replaces only that network’s traces). **Manual draw** closes the dialog and starts the canvas polyline with the same property set. Existing joints can be loaded or deleted from the list. Peak kn / ks / c / φ copy onto the feature so the `.dat` Joints block and canvas overlay stay consistent. See [Joint networks](joint-networks.md).

### Drawing workflow

1. Open the Geometry Definition tab.
2. Prefer **Geometry → Add Joint…** to place a joint at a suitable location, or **Draw Weak Plane…** (Bedding / Tension Crack) to digitize by hand.
3. For hand draw: click successive points on the canvas (snap applies: vertex, edge, grid, ortho), then **Finish Geological Feature**.
4. Run **Geometry → Verify Geometry**, then mesh and run FE analysis. Joint properties export in the `.dat` Joints block.

## True fill / cut (kernel booleans)

Surface boolean dialogs (Union / Subtract / Intersect) and fill overlays can be committed into the solid kernel:

1. Create excavation or fill operations as usual on Geometry / staging.
2. Run **Geometry → Commit Kernel Booleans**.
3. Verify geometry; assign materials to any new fill faces.
4. Save the project (history and geological features persist in format 1.8+).

Undo records a single coalesced snapshot per commit via the project undo host.

## Interchange formats

| Format | Import | Export | Notes |
|--------|--------|--------|-------|
| **CSV** | Yes | Yes | Structured points/lines/surfaces or legacy x,y |
| **DXF** | Yes (wizard) | Yes (AC1015 ASCII) | R12 POLYLINE / missing `$ACADVER` repaired or read via ASCII fallback |
| **LandXML** | Yes (surfaces / lines) | Yes | Minimal Surface / Line geometry |
| **GeoJSON** | Yes (Polygon / LineString) | Yes | Optional EPSG / project CRS |
| **DWG** | No (detect + guide) | No | Save As DXF (AutoCAD 2000+) or convert with ODA File Converter |

### Menus

| Action | Menu |
|--------|------|
| Import | **File → Import → Geometry (CSV / DXF / LandXML / GeoJSON)…** (`Ctrl+Shift+G`) |
| Export | **File → Export → Geometry (DXF / CSV / LandXML / GeoJSON)…** |

### After import

1. Confirm extents under **Project Settings → Grid & Axes**.
2. Run **Geometry → Heal Geometry** if vertices nearly touch but do not meet.
3. Close open rings that should form the outer soil boundary; build surfaces/regions.
4. Review geological polylines imported as open chains (layer names such as WeakPlane).
5. **Geometry → Verify Geometry**, then assign materials.
6. **File → Save**.

> **Warning:** Import does not invent valid meshable regions. Unclosed loops and crossing edges must be cleaned before meshing.

## Project coordinate system (CRS)

Optional georeferencing lives on the project model (**EPSG** code, name, origin easting/northing, rotation, scale). When set, GeoJSON import/export can map between local model (X, Y) and map easting/northing. DXF placement still uses the import wizard’s units, scale, offset, and rotation.

## CAD editing helpers

| Capability | Where |
|------------|--------|
| Snap (grid, vertex, midpoint, intersection, edge, ortho) | **Geometry → Snap Settings…** |
| Heal near-coincident vertices | **Geometry → Heal Geometry** |
| Measure | View / Geometry measure tool (existing) |
| Multi-select move / rotate / scale | Available to commands and scripts via geometry CAD edit helpers |
| Copy / paste geological features | Clipboard helpers for cross-project paste (offset supported) |

A Field Stations **soil block** is a closed rectangle. To form the ground line from that box, edit vertices, Draw Lines, or Surface **Excavate** then **Geometry → Commit Kernel Booleans**. See [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md).

## Typical production workflow

1. Import survey DXF or LandXML; set CRS if using GeoJSON map data. Alternatively start from Field Stations **Create soil block**.
2. Heal gaps; verify topology.
3. Boolean excavate benches (or cut the slope from a soil-block rectangle); **Commit Kernel Booleans**; fill if needed.
4. Draw weak plane and tension crack if the geology requires them.
5. Mesh the domain; apply loads and boundary conditions.
6. Run FE analysis; export DXF or GeoJSON for CAD/GIS round-trip.

## See also

- [Importing geometry](import.md)
- [Boundaries](boundaries.md)
- [Excavations and tunnels](excavation-tunnel.md)
- [The .xfem file format](../reference/xfem-format.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
