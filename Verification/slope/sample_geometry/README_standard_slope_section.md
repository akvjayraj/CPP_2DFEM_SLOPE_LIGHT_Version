# Standard slope section DXF

File: `standard_slope_section.dxf` (valid netDxf-generated; loads in ~150 ms)

> **Note:** An earlier hand-written ASCII DXF in this folder could cause **netDxf to hang** during import (UI appears frozen for minutes). Always use DXF files saved from AutoCAD, LibreCAD, or netDxf—not minimal hand-edited ASCII.

Alternate copy: `standard_slope_section_netdxf.dxf`

## Geometry (axis units: meters)

Homogeneous 2D slope / embankment cross-section:

| Vertex | X (m) | Y (m) | Description |
|--------|-------|-------|-------------|
| 1 | 0 | 0 | Left toe |
| 2 | 0 | 12 | Base of slope face |
| 3 | 18 | 24 | Break of slope (approx. 2H:1V face) |
| 4 | 60 | 24 | Crest |
| 5 | 90 | 0 | Right toe |

## DXF entities

| Layer | Entity | Purpose |
|-------|--------|---------|
| `BOUNDARY` | Closed LWPOLYLINE (5 vertices) | External soil region → **Surface S1** on import |
| `LAYER_LINE` | LINE (0,8)–(90,8) | Horizontal stratum / layer interface (internal line) |
| `SLOPE_FACE` | LINE (0,12)–(18,24) | Slope face reference (internal line) |
| `POINTS` | POINT at each corner | Reference nodes |

## Import in app

1. **File → Import → Geometry (CSV / DXF)...** or **Ctrl+Shift+G**
2. Select `standard_slope_section.dxf`
3. In **DXF Import Options**, enable:
   - Import points
   - Import lines and polylines
   - Import closed regions as surfaces
4. Click **Import**

Expected result: closed boundary polygon with 5 vertices, internal chord(s), and locked surface from the closed polyline.
