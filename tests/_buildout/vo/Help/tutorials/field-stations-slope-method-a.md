# Tutorial: Cut the slope — Method A (drag vertices)

Reshape the **soil-block** rectangle on **Geometry Definition** by adding vertices on the **top** edge and dragging them onto the borehole collars.

Open it from **Help → Tutorials...**.

## Before you start

1. Finish [Field Stations — borehole log to soil block](field-stations-import.md): import `embankment-evidence-pack.csv`, leave **Reference elevation = 0**, **Create soil block**, optionally **Commit interfaces**.
2. Open **Geometry Definition**. Click the **Plot boreholes** toolbar button (after **Add Circle**) so the logs show on that canvas.
3. **Geometry → Snap Settings…** — turn on vertex and grid snap.

Example files (next to the application, under `Help/examples/field-stations/`):

| File | Use |
|------|-----|
| [embankment-evidence-pack.csv](../examples/field-stations/embankment-evidence-pack.csv) | Shared logs |
| [method-a-target-ground.csv](../examples/field-stations/method-a-target-ground.csv) | Finished outer ring (import as a check, or type these vertices) |

Soil block (margin 5): **(3, −5) (45, −5) (45, 25) (3, 25)**. Ground must pass through **Crest (8, 20)**, **Mid-slope (25, 15)**, **Toe (40, 10)**.

![Start: closed soil block with three borehole pins](images/fs-slope-start-soil-block.png)

If you set **Reference elevation = 100**, add 100 to every Y in this tutorial.

## 1. Turn on vertex edit

1. On Geometry Definition, click **Points** (left strip). The Points window opens.
2. Click **Edit**. The canvas is now in vertex-edit mode. **Finish** turns that mode off.
3. Turn **Pan** off (toolbar or **P**, then **Escape**). Pan captures the left button, so a drag will pan the view instead of moving a point.
4. **Geometry → Snap Settings…** — keep vertex snap on so a drag can latch onto a borehole collar.

You can only **drag vertices that already exist** (the numbered dots 1–4 on the soil block). Clicking the top *edge* does not insert a point on that edge.

## 2. How to drag a vertex

1. Move the cursor onto a numbered vertex until it is under the pointer (grab tolerance is about 10 pixels).
2. **Press and hold** the left mouse button — do not click and release.
3. **Move** the mouse. The vertex follows. A snap marker appears when the cursor latches onto a borehole pin or another vertex.
4. **Release** the button to drop the point. **Ctrl+Z** undoes the whole drag as one step.

Repeat for each corner you want to move. Typical first moves on this example:

| Drag this corner | Onto |
|------------------|------|
| Top-left **(3, 25)** | **(3, 20)** — left side at crest height |
| Top-right **(45, 25)** | **(45, 10)** — right side at toe height |

That only places two ground points. Crest, Mid-slope, and Toe still need extra vertices (next step).

## 3. Add the collar vertices, then drag them

The soil block has four corners. Add three more points, then drag each onto a pin:

1. In the Points window click **New** (or stay in **Edit** and click empty canvas to append a vertex).
2. Click once to place a point, then **press and hold** that new numbered dot and drag it onto the pin.

| Vertex | Drag onto | Station |
|--------|-----------|---------|
| New | **(8, 20)** | Crest (BH-C) |
| New | **(25, 15)** | Mid-slope (BH-M) |
| New | **(40, 10)** | Toe (BH-T) |

The blue **S1** fill follows those points as you drag — it is no longer stuck to the original four-corner rectangle. Alternatively, type **X** and **Y** in the Points grid, or **File → Import → Geometry…** [method-a-target-ground.csv](../examples/field-stations/method-a-target-ground.csv).

![Add vertices, then press-and-hold each numbered dot and drag onto a collar](images/fs-method-a-drag.png)

## 4. Check the closed ring

The outer boundary should be:

```text
(3, −5) → (45, −5) → (45, 10) → (40, 10) → (25, 15) → (8, 20) → (3, 20) → close
```

That is the same ring as [method-a-target-ground.csv](../examples/field-stations/method-a-target-ground.csv). You may **File → Import → Geometry…** that file to replace the rectangle if you only want to verify the target.

![Finished Method A slope: ground 20 → 15 → 10](images/fs-method-a-done.png)

## 5. Verify

1. **Geometry → Verify Geometry**.
2. Assign any leftover regions on **Material Properties**.
3. Continue with mesh and FE analysis as in [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md).

## See also

- [Method B — draw lines](field-stations-slope-method-b.md)
- [Method C — excavate](field-stations-slope-method-c.md)
- [Boundaries](../geometry/boundaries.md)
