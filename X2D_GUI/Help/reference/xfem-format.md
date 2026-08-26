# The .xfem file format

Native project files use the **`.xfem`** extension. They are **UTF-8 JSON**.

## Versions

`formatVersion` may be `"1.10"` (current) down through `"1.0"`. Older files load with defaults for any fields they lack. Unknown JSON properties are ignored on deserialize.

Notable bumps:

| Version | Highlights |
|---------|------------|
| **1.10** | Field Stations evidence pack under LEM settings (`fieldStations`: station X, unit stacks, unit→material map with `colourArgb`; `plotBoreholes` canvas overlay; `fieldStationSoilBlockMargin`; `fieldStationReferenceElevation`). Commit writes existing layer cuts; logs are not embedded as base64 |
| **1.9** | Georeferenced slope-image underlay: `geometry.slopeImageUnderlay` calibration plus `{project}.underlay.png` sidecar next to the `.xfem` |
| **1.8** | Geological features (weak plane / bedding / tension crack), kernel boolean history, project coordinate system (CRS), composite weak-plane follow flag |
| **1.7** | Results Visualization tab removed from the main strip (Sensitivity / Report / Script indices shift) |
| **1.6** | Explicit SI storage note; persisted LEM search / sensitivity / back-analysis results |
| **1.5** | LEM back-analysis setup |
| **1.3** | Arc segment metadata, surface `assignedMaterialId`, reliable geometry flush on save |

## Main sections

| Section | Contents |
|---------|----------|
| `main` | Selected tab, zoom/pan, current stage |
| `projectSettings` | General / analysis / grid, stage footer, axis limits, LEM analysis settings, optional CRS |
| `geometry` | Vertices, segments, surfaces, excavations, tunnels, layer paint, geological features, joint property sets, optional joint-network definitions, kernel boolean history, optional slope-image underlay |
| `materials` | Material library, failure criteria, probabilistic stats |
| `loads` / `fieldStress` | Surface loads and field-stress settings |
| `feAnalysis` / `stagedConstruction` | FE options, stages, SRF trials, pseudo-static stage coefficients |
| `results` / `verification` / `script` | FE snapshot, verification UI, Script Editor content |

LEM-specific options (search method, methods checklist, seismic mode including Newmark inputs, groundwater mode, probabilistic sampling, support elements, composite follow, Field Stations) live under the project / LEM settings portion of the snapshot.

## Save behaviour

- **File → Save** writes to the last Open / Save As path; otherwise it opens Save As.
- **Save As** and **Save a Copy** write a full snapshot.
- Autosave may write a sidecar when enabled in the application.

## Slope-image underlay (1.9+)

When a raster underlay is present, Save / Save As writes a PNG sidecar named `{projectName}.underlay.png` beside the `.xfem`. The JSON stores a relative path plus two-point or axis-aligned calibration (`mode`, image pixel size, world points or world bounds, opacity, visibility). Keep the sidecar with the project when copying folders.

## Compatibility notes

- Prefer opening `.xfem` files with the same or a newer application build.
- Absolute paths (for example a Newmark time-history file) are stored as written; move or re-browse them if you relocate the project.
- Geometry interchange files (DXF, LandXML, GeoJSON) are separate from `.xfem`; import them into Geometry Definition, then save the project.
- Slope-image underlays use a sidecar PNG; relocating only the `.xfem` without `{project}.underlay.png` drops the underlay on open.

## See also

- [Projects and files](../getting-started/project-files.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
- [Verification suite](verification-suite.md)
