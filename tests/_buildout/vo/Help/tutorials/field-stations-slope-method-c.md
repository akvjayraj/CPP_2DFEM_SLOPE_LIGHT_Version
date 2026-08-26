# Tutorial: Cut the slope — Method C (excavate)

Keep the soil-block rectangle and **subtract the air** above the collars with Surface **Excavate**, then **Commit Kernel Booleans**.

Open it from **Help → Tutorials...**.

## Before you start

1. Finish [Field Stations — borehole log to soil block](field-stations-import.md): import `embankment-evidence-pack.csv`, leave **Reference elevation = 0**, **Create soil block**.
2. Open **Geometry Definition**. Click the **Plot boreholes** toolbar button (after **Add Circle**) so the logs show on that canvas.
3. **Geometry → Snap Settings…** — vertex snap on.

Example files:

| File | Use |
|------|-----|
| [embankment-evidence-pack.csv](../examples/field-stations/embankment-evidence-pack.csv) | Shared logs |
| [method-c-excavate.csv](../examples/field-stations/method-c-excavate.csv) | Air-polygon vertices (click in this order) |

![Start: soil block with padding above the pins](images/fs-slope-start-soil-block.png)

If you set **Reference elevation = 100**, add 100 to every Y.

## 1. Draw the air polygon

1. Click **Surface** → **Excavate**.
2. Click these vertices (the hole you are cutting is the padding above the ground):

| # | X | Y | Note |
|--:|--:|--:|------|
| 1 | 3 | 20 | Left side at crest height |
| 2 | 8 | 20 | Crest collar |
| 3 | 25 | 15 | Mid-slope collar |
| 4 | 40 | 10 | Toe collar |
| 5 | 45 | 10 | Right side at toe height |
| 6 | 45 | 25 | Top-right of the soil block |
| 7 | 3 | 25 | Top-left of the soil block |

Close back to the first point. The same list is in [method-c-excavate.csv](../examples/field-stations/method-c-excavate.csv).

![Excavate the air polygon above 20 → 15 → 10](images/fs-method-c-excavate.png)

## 2. Commit the boolean

1. **Geometry → Commit Kernel Booleans**.
2. The rectangle top disappears. Soil remains below **20 → 15 → 10**.

![After Commit Kernel Booleans the slope remains](images/fs-method-c-done.png)

## 3. Verify

1. **Geometry → Verify Geometry**.
2. Assign materials. Mesh and run FE analysis as in [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md).

> **Note:** Excavate removes material. Do not draw the air polygon *below* the collars or you will cut the soil layers.

## See also

- [Method A — drag vertices](field-stations-slope-method-a.md)
- [Method B — draw lines](field-stations-slope-method-b.md)
- [Excavations and tunnels](../geometry/excavation-tunnel.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
