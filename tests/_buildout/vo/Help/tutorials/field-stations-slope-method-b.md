# Tutorial: Cut the slope — Method B (draw lines)

Draw the ground as **Lines** through the borehole collars, then lock a closed **Surface**. Use this when you prefer digitising a polyline instead of dragging box corners.

Open it from **Help → Tutorials...**.

## Before you start

1. Finish [Field Stations — borehole log to soil block](field-stations-import.md): import `embankment-evidence-pack.csv`, leave **Reference elevation = 0**, **Create soil block**.
2. Open **Geometry Definition**. Click the **Plot boreholes** toolbar button (after **Add Circle**) so the logs show on that canvas.
3. **Geometry → Snap Settings…** — vertex snap on.

Example files:

| File | Use |
|------|-----|
| [embankment-evidence-pack.csv](../examples/field-stations/embankment-evidence-pack.csv) | Shared logs |
| [method-b-slope-lines.csv](../examples/field-stations/method-b-slope-lines.csv) | Ground click list (X, Y) |

![Start: soil block with pins at 20, 15, and 10](images/fs-slope-start-soil-block.png)

If you set **Reference elevation = 100**, add 100 to every Y.

## 1. Draw the ground polyline

1. Click **Lines**.
2. Click these points **left to right** (snap to pins where they coincide):

| # | X | Y | Note |
|--:|--:|--:|------|
| 1 | 3 | 20 | Left side of the soil block |
| 2 | 8 | 20 | Crest collar |
| 3 | 25 | 15 | Mid-slope collar |
| 4 | 40 | 10 | Toe collar |
| 5 | 45 | 10 | Right side of the soil block |

The same list is in [method-b-slope-lines.csv](../examples/field-stations/method-b-slope-lines.csv).

![Draw Lines through the three collars](images/fs-method-b-lines.png)

## 2. Close the soil region

The box already has a base and two sides. After the ground line is in, every soil loop must be closed:

1. Click **Surface**.
2. Pick the outer-ring segments (or use **Add Surface**, which preselects `L1…Ln` when a closed boundary exists).
3. Click Action **Add** to lock **S1**.

![Surface Add locks the closed soil region](images/fs-method-b-surface.png)

If a leftover rectangular cap remains above the ground, delete those top-edge segments or use [Method C](field-stations-slope-method-c.md) to subtract the air.

## 3. Verify

1. **Geometry → Verify Geometry**.
2. Assign materials. Mesh and run FE analysis as in [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md).

## See also

- [Method A — drag vertices](field-stations-slope-method-a.md)
- [Method C — excavate](field-stations-slope-method-c.md)
- [Boundaries](../geometry/boundaries.md)
