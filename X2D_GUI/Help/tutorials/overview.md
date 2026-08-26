# Tutorials

Illustrated, step-by-step walkthroughs that take you from an empty (or sample) project to a finished result. Prefer these when you want screenshots; use [Quick start (FEM)](../getting-started/quick-start-fem.md) for a shorter text-only checklist.

## Prerequisites

- **Unit system:** SI (stored internally even if you later switch the display to Imperial).
- **Analysis type:** Deterministic 2D FEM for the tutorials below.
- **Help menu:** **Help → Tutorials...** opens this chapter; **F1** still opens context help for the active tab.

## Available tutorials

| Tutorial | What you learn |
|----------|----------------|
| [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md) | Draw or import geometry, assign materials, mesh, apply BC, run FE Analysis, review results |
| [Field Stations — borehole log to soil block](field-stations-import.md) | Import the bundled Evidence Pack, create a closed soil block, commit or draw layers |
| [Cut the slope — Method A (drag vertices)](field-stations-slope-method-a.md) | Reshape the soil-block top through Crest 20, Mid-slope 15, Toe 10 |
| [Cut the slope — Method B (draw lines)](field-stations-slope-method-b.md) | Draw the ground polyline, then Surface Add |
| [Cut the slope — Method C (excavate)](field-stations-slope-method-c.md) | Subtract the air above the collars, then Commit Kernel Booleans |

After the slope is cut and materials are assigned, continue with the Mesh → Boundary Conditions → FE Analysis → Results path in the [FEM quick start](../getting-started/quick-start-fem.md).

## Coming later

Additional illustrated tutorials (seepage, SRF on a homogeneous slope, tunnel excavation) will appear here as they are written. Until then, use the related chapters in the contents tree.

## See also

- [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md)
- [Tour of the interface](../getting-started/interface-tour.md)
- [Finite element analysis](../fem/overview.md)
- [FE results](../fem/fe-results.md)
