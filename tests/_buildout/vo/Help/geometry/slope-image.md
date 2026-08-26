# Slope image underlay

Import a photo or scanned drawing of a slope onto the Geometry Definition canvas, calibrate it to model coordinates, then digitise ordinary vertices and surfaces on top with the usual draw tools.

## Import

1. On the Geometry tab, click the toolbar **Slope Image** button (immediately after **Add Circle**), or choose **File → Import → Slope Image…** (also **Geometry → Import Slope Image…**).
2. In the wizard, click **Browse…** and pick a **PNG**, **JPG**, **BMP**, or **TIFF** file.
3. Calibrate the image to axis **X / Y**:
   - **Two-point** (default): click two image points and enter their world coordinates. This sets translation, rotation, and uniform scale.
   - **Axis-aligned bounds**: enter the lower-left and upper-right world coordinates of the image rectangle (no rotation). Use this for simple screenshots.
4. Adjust **Opacity** and **Show underlay** if needed, then **Import**. Existing geometry is kept by default; the Geometry axis **fits** to the image extent (with a small border) so the full underlay is clearly visible.

If an underlay is already in the project, the same command reopens the wizard prefilled. The primary button is **Apply**. Use **Browse…** to replace the file, or **Clear underlay** to remove the image (geometry is unchanged).

## Digitise on the image

The underlay is a visual guide only — there is no automatic edge tracing.

1. Trace the ground profile with **Add vertex**, **Draw line**, or polyline / surface pick tools.
2. Use **Draw arc** for circular arcs that appear on the drawing.
3. Close the outer ring and create / lock surfaces as usual (**Add Surface** preselects the outer-ring `L#` segments when they exist).
4. Run **Geometry → Verify Geometry** before analysis.

While an underlay is present, **edge / midpoint / intersection** snap is turned off so new points follow the cursor on the image instead of latching onto already-drawn polylines. **Vertex** snap (and grid/ortho, if enabled) still work so you can close cleanly onto existing endpoints.

## Wizard controls

All underlay controls live in the Slope Image wizard (toolbar **Slope Image** or **File → Import → Slope Image…**). There is no Slope image block on the Geometry side panel.

| Control | Action |
|---------|--------|
| **Browse…** | Pick or replace the image file |
| **Show underlay** | Toggle visibility on the Geometry canvas after apply |
| **Opacity** | Fade the image so digitised lines stay readable |
| **Clear underlay** | Remove the image from the project and close the wizard (geometry is unchanged) |
| **Import** / **Apply** | Commit calibration. **Import** for a new file; **Apply** when an underlay already exists |

## Project files

Saving the project (format **1.9+**) writes:

- Calibration and path inside the `.xfem` under `geometry.slopeImageUnderlay`
- A PNG sidecar next to the project: `{projectName}.underlay.png`

Keep the sidecar beside the `.xfem` when you copy or archive the project. Relocating only the `.xfem` without the sidecar will drop the underlay on open.

## See also

- [Importing geometry](import.md)
- [Boundaries](boundaries.md)
- [The .xfem file format](../reference/xfem-format.md)
