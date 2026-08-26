# Importing geometry

You can bring external outlines into Geometry Definition instead of digitising every vertex by hand. Companion export writes the same geometry for CAD/GIS round-trip.

## Supported formats

| Format | Import | Export | Menu |
|--------|--------|--------|------|
| **CSV** | Yes | Yes | **File → Import / Export → Geometry…** |
| **DXF** | Yes (wizard) | Yes (AC1015 ASCII) | Same; DXF opens the import wizard |
| **LandXML** | Yes | Yes | Same (`.xml`) |
| **GeoJSON** | Yes | Yes | Same (`.geojson` / `.json`) |
| **Slope image** | Underlay (PNG/JPG/…) | Sidecar PNG on save | **File → Import → Slope Image…** |
| **DWG** | Not native | — | Select a `.dwg` to see conversion guidance |

Raster underlays are calibrated to X/Y and digitised with the ordinary draw tools — see [Slope image underlay](slope-image.md).

Shortcut: **Ctrl+Shift+G** opens the import file picker.

Full CAD-grade workflow (heal, kernel booleans, geological layers, CRS) is described in [Production geometry and interchange](production-geometry.md).

## DXF import wizard

1. Choose a `.dxf` file.
2. Wait for the drawing to load (legacy R12 POLYLINE files and missing `$ACADVER` headers are repaired or read with an ASCII fallback so the wizard can open).
3. Set layer roles, units, scale, offset, rotation, and mirror as needed.
4. Preview, then apply into Geometry Definition.

> **Note:** Binary DXF older than AutoCAD 2000 and native DWG are not supported. In CAD, use **Save As → ASCII DXF, AutoCAD 2000 or later**.

## CSV, LandXML, and GeoJSON

| Source | Behaviour |
|--------|-----------|
| **CSV** | Structured `[POINTS]` / `[LINES]` / `[SURFACES]` tables, or legacy x,y columns. Applied directly (confirm replace). |
| **LandXML** | Surfaces and line geometry mapped into the planar kernel, then into editable geometry. |
| **GeoJSON** | `Polygon` / `LineString` features; optional `role` property (`weakplane`, `bedding`, `tensioncrack`, …). Uses the project CRS when EPSG/origin are set. |

## After import

1. Confirm the geometry appears inside the current axis window; adjust **Grid & Axes** if needed.
2. Run **Geometry → Heal Geometry** if imported corners nearly touch but do not meet.
3. Close any open polylines that should form the outer soil boundary.
4. Build or refresh **surfaces / regions** from the imported segments.
5. Review any geological polylines (weak plane / bedding / crack).
6. Run **Geometry → Verify Geometry**.
7. Assign materials on the Material Properties tab.
8. **File → Save** to persist inside the `.xfem` project (format 1.8+ stores geological features and kernel boolean history; format 1.9+ also stores a slope-image underlay sidecar).

> **Warning:** Import does not automatically create valid meshable regions. Unclosed loops and duplicate vertices must be cleaned before meshing.

## Export

**File → Export → Geometry (DXF / CSV / LandXML / GeoJSON)…** writes the current boundary, regions, and geological features (format-dependent layers/properties).

## Related file operations

- Material databases import separately under Material Properties.
- **Field Stations Evidence Pack (CSV)** is a separate import on Project Settings → Analysis → Field Stations. It can create a closed soil block instead of (or before) a CAD outline — see [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md).
- Results and reports export from the Results / Report Generation workflows and do not replace the geometry import path.

## See also

- [Slope image underlay](slope-image.md)
- [Production geometry and interchange](production-geometry.md)
- [Boundaries](boundaries.md)
- [Projects and files](../getting-started/project-files.md)
- [Grid and axes](../project-settings/grid-axes.md)
- [The .xfem file format](../reference/xfem-format.md)
