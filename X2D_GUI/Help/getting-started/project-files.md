# Projects and files

GeotechSlopeApp stores each model as a single project file with the **`.xfem`** extension.

## File format

- Encoding: **UTF-8**
- Structure: **JSON** document
- Typical contents: project settings, geometry, materials, loads, groundwater, support, surface search settings, mesh/FE data when used, script text, and saved result snapshots

> **Note:** Unknown JSON properties are ignored when loading older or newer files, so forward-compatible fields can be added without breaking open.

## Creating and saving

| Command | Behaviour |
|---------|-----------|
| **File → New Project** | Starts a new empty project |
| **File → Open** | Loads an existing `.xfem` file |
| **File → Save** | Writes to the current path; if none, opens Save As |
| **File → Save As** / **Save a Copy** | Writes a full project snapshot to a chosen path |
| **File → Close Project** | Closes the active project |

Save captures the state of the open tabs, including the selected main tab, zoom/pan where applicable, and the current construction stage.

## Import and export companions

Besides the native project file, File and Geometry menus expose helpers such as:

- **Geometry (CSV / DXF / LandXML / GeoJSON)** import (`Ctrl+Shift+G`)
- **Field Stations Evidence Pack (CSV)** on Project Settings → Analysis → Field Stations (see [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md))
- **Geometry (DXF / CSV / LandXML / GeoJSON)** export
- Material database import/export
- Results (CSV / VTK) and Report (PDF) export

Imported geometry (including geological features) becomes part of the project once you save the `.xfem` file (format **1.8+**). See [Importing geometry](../geometry/import.md) and [Production geometry](../geometry/production-geometry.md).

## Units

Unit System (**SI** or **Imperial**) is stored with the project under Project Settings → General. Values are persisted in SI; the UI converts for Imperial display and editing.

## See also

- [Project Settings — General](../project-settings/general.md)
- [Importing geometry](../geometry/import.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
- [The .xfem file format](../reference/xfem-format.md)
- [Overview](overview.md)
