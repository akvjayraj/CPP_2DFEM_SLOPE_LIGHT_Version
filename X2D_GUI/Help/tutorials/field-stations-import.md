# Tutorial: Field Stations — borehole log to soil block

This walkthrough imports a **Field Stations Evidence Pack** (proprietary CSV borehole logs), builds a **closed soil block** around the holes, then either **commits horizontal layers** or **draws non-horizontal interfaces**. Cut the slope from the box with Geometry Definition.

Open it from **Help → Tutorials...** or the Tutorials chapter in **Help → User Manual...**.

### Workflow

1. Import Evidence Pack → pins and unit stacks on the canvas.
2. Leave **Reference elevation = 0** for the slope-cutting tutorials (or set **100** and add 100 to every Y later). **Create soil block** — closed rectangle around the logs plus **Block margin**.
3. Map each unit to a material (and colour).
4. **Either** **Commit interfaces** (horizontal cuts) **or** **Draw non-horizontal layer…**.
5. Cut the slope with one of [Method A](field-stations-slope-method-a.md), [Method B](field-stations-slope-method-b.md), or [Method C](field-stations-slope-method-c.md).
6. **Verify Geometry**, assign remaining regions, mesh, and run FE analysis.

Field Stations stay **evidence**. After commit or a drawn layer, the FE model uses the same layer cuts and material assignments as any other model. Commit does **not** auto-connect stations into sloping polylines.

Sample files ship with the manual (next to the application, under `Help/examples/field-stations/`):

| File | Use |
|------|-----|
| [embankment-evidence-pack.csv](../examples/field-stations/embankment-evidence-pack.csv) | Logs as Top Y / Bottom Y (**Reference elevation = 0** for Methods A–C) |
| [embankment-evidence-pack-depth.csv](../examples/field-stations/embankment-evidence-pack-depth.csv) | Same logs as Collar Y − Depth |
| [embankment-geometry.csv](../examples/field-stations/embankment-geometry.csv) | Optional finished slope if you skip A–C |
| [method-a-target-ground.csv](../examples/field-stations/method-a-target-ground.csv) | Method A finished ring |
| [method-b-slope-lines.csv](../examples/field-stations/method-b-slope-lines.csv) | Method B ground clicks |
| [method-c-excavate.csv](../examples/field-stations/method-c-excavate.csv) | Method C air polygon |

This is not gINT, AGS, GEO5, or Slide import. Map columns once in the Evidence Pack wizard.

## Model (SI metres)

Three stations, same unit sequence (FILL over CLAY over SAND). Leave **Reference elevation = 0** so imported Y is model Y. Interior contacts are **Y = 14** and **Y = 8**. Soil block (margin 5): **(3, −5) (45, −5) (45, 25) (3, 25)**.

| Station | X | Collar Y | Stack (top → bottom) |
|---------|--:|---------:|----------------------|
| BH-C Crest | 8 | 20 | FILL 20–14 · CLAY 14–8 · SAND 8–0 |
| BH-M Mid-slope | 25 | 15 | FILL 15–14 · CLAY 14–8 · SAND 8–0 |
| BH-T Toe | 40 | 10 | CLAY 10–8 · SAND 8–0 (no fill) |

**Create soil block** makes that rectangle. After **Commit interfaces**, Layer 1 = SAND (bottom), Layer 2 = CLAY, Layer 3 = FILL. Optional: set **Reference elevation = 100** and add 100 to every Y in Methods A–C.

![Soil block and borehole pins (Reference elevation 0)](images/fs-slope-start-soil-block.png)

Suggested Mohr–Coulomb starters (create with **Create material** if the library is empty):

| Unit | γ (kN/m³) | c′ (kPa) | φ′ (°) |
|------|----------:|---------:|-------:|
| FILL | 18 | 5 | 30 |
| CLAY | 19 | 15 | 18 |
| SAND | 20 | 0 | 35 |

---

## 1. New project and axes

1. **File → New Project**.
2. Project Settings → **General**: Unit System **SI**.
3. **Analysis Type**: Deterministic, **2D Problem**.

## 2. Import the Evidence Pack

1. Project Settings → **Analysis** → **Field Stations**.
2. Turn on **Field Stations active**. Leave **Plot boreholes on canvas** checked, then use the **Plot boreholes** toolbar button on Geometry or Material Properties to show the columns on that canvas.
3. Leave **Reference elevation** at **0** and **Block margin** at 5 (required for the Method A / B / C figures).
4. **Import…** and select `embankment-evidence-pack.csv`.
5. Confirm columns: Station Id, X, Top Y, Bottom Y, Unit code (Label and Note optional). Preview three rows, then **Import**.

If Geometry was empty, a closed soil block appears automatically. Otherwise click **Create soil block**. Pins sit at Crest **20**, Mid-slope **15**, Toe **10**. The empty area above the pins is **padding**, not the ground. **Plot boreholes** is overlay only.

To practise depth logs instead, import `embankment-evidence-pack-depth.csv` and choose **Depths (Collar Y − Depth)**.

## 3. Map units to materials

In **Unit → Material**, map FILL, CLAY, and SAND. Each unit has a **Colour** swatch (double-click to change). If the dropdowns are empty, click **Create material** once. Then set strength on **Material Properties**.

Status becomes **Commit ready** when every unit has a material.

## 4. Layers inside the block

**Horizontal:** click **Commit interfaces**. That writes horizontal cuts at Y = **8** and **14** and assigns Layer 1–3 bottom→top.

**Non-horizontal:** click **Draw non-horizontal layer…**, then click through borehole contacts (the cursor snaps to unit Top Y / Bottom Y). Keep **Extend outer contacts to model boundary** on so the polyline reaches the left and right of the box. Finish with **Add Layer** on Material Properties → Layering (Non-Horizontal). Assign materials there.

You can use both: Commit for flat contacts, then draw additional inclined interfaces.

## 5. Cut the slope

The soil block is a rectangle. The ground line should pass through **20 → 15 → 10**. Pick one illustrated tutorial:

| Method | What you do | Tutorial |
|--------|-------------|----------|
| **A** | Add / drag vertices on the top edge onto the pins | [Method A — drag vertices](field-stations-slope-method-a.md) |
| **B** | Draw Lines through the collars, then Surface Add | [Method B — draw lines](field-stations-slope-method-b.md) |
| **C** | Excavate the air above the collars, then Commit Kernel Booleans | [Method C — excavate](field-stations-slope-method-c.md) |

Optional shortcut: import [embankment-geometry.csv](../examples/field-stations/embankment-geometry.csv) if you want a finished outline without digitising.

## 6. Verify, then analyse

1. **Geometry → Verify Geometry**.
2. Assign any remaining regions if needed.
3. Mesh the domain and run FE analysis as in [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md).

## See also

- [Analysis Settings — Field Stations](../analysis/analysis-settings.md#field-stations)
- [Layers and assignment](../materials/layer-assignment.md)
- [Production geometry and interchange](../geometry/production-geometry.md)
- [Cut the slope — Method A](field-stations-slope-method-a.md)
- [Cut the slope — Method B](field-stations-slope-method-b.md)
- [Cut the slope — Method C](field-stations-slope-method-c.md)
- [Importing geometry](../geometry/import.md)
