# Grid and axes

The **Grid & Axes** page defines the model-space window used for drawing and display.

## Controls

| Setting | Meaning |
|---------|---------|
| **X-Axis Minimum / Maximum** | Horizontal extent of the model view |
| **Y-Axis Minimum / Maximum** | Vertical extent of the model view |
| **Grid Spacing** | Spacing of the drawing grid (stored in metres in the model) |

Show Grid and Show Axes toggles (project settings / view commands) control whether the grid and axes are drawn on geometry canvases. Axes can also be toggled from the View menu (F10).

## Choosing extents

Set limits that enclose the full soil boundary with a modest margin so:

- Snap and digitising stay inside the visible window.
- Slip-surface search grids and overlays remain readable.
- Imported DXF/CSV geometry is not clipped from view.

> **Tip:** After import, expand axis limits if the geometry lies outside the current window, then re-centre the view on the Geometry Definition tab.

## See also

- [Project Settings — General](general.md)
- [Boundaries](../geometry/boundaries.md)
- [Importing geometry](../geometry/import.md)
