# Project Settings — General

The **General** page of the Project Settings tab stores project identity and the unit system used in the interface.

## Fields

| Field | Purpose |
|-------|---------|
| **Project Title** | Name shown with the project metadata |
| **Author** | Author or organisation string |
| **Created On** | Project creation date |
| **Unit System** | **SI** or **Imperial** |

## Unit system

- The analysis model stores values in **SI**.
- When **Imperial** is selected, length, stress, unit weight, and related quantities are converted for display and editing.
- Changing units does not alter the underlying SI storage; it changes how values appear and how typed values are interpreted.

> **Note:** Prefer setting the unit system before entering many numeric properties so labels and conversions stay consistent from the start.

## Coordinate system (CRS)

Projects can store an optional **coordinate system** used with GIS interchange (GeoJSON):

| Property | Purpose |
|----------|---------|
| **EPSG code** | Optional projected CRS identifier (for example UTM) |
| **Name** | Display name (for example “Local engineering (metres)”) |
| **Origin easting / northing** | Ground-control origin for map ↔ model mapping |
| **Rotation / scale** | Optional similarity transform |

Local model coordinates remain engineering (X, Y) in metres (SI storage). When a CRS is set, GeoJSON import/export can map between model coordinates and map easting/northing. DXF import still uses the wizard’s units and placement transforms. See [Production geometry and interchange](../geometry/production-geometry.md).

## Related Project Settings pages

Project Settings also contains:

- **Analysis Type** — problem dimension, deterministic/probabilistic mode, and Analysis Settings pages
- **Grid & Axes** — drawing extents and grid spacing

## See also

- [Grid and axes](grid-axes.md)
- [Analysis type](analysis-type.md)
- [Importing geometry](../geometry/import.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
- [Projects and files](../getting-started/project-files.md)
