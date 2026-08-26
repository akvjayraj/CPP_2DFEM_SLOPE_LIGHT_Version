# p610_t3 — standalone sample

Self-contained folder for running the `p610_t3` elasto-plastic T3 excavation
solver without launching the GUI.

## Contents

| File           | Description                                                                |
| -------------- | -------------------------------------------------------------------------- |
| `p610_t3.exe`  | Solver binary (copy of `build/Release/p610_t3.exe`).                       |
| `sample.dat`   | 1759 T3, 2 stages: (1) small pilot / circular opening (18 T3) only, main cavern still solid; (2) main large cavern (334 T3) — pilot from stage 1 is not re-listed, only new chamber elements.  |

## Run

From this folder:

```powershell
.\p610_t3.exe sample
```

The solver reads `sample.dat`, then writes two files next to it:

| File           | Description                                                              |
| -------------- | ------------------------------------------------------------------------ |
| `sample.res`   | Human-readable text results (per-layer, per-increment, output-node disp).|
| `sample.json`  | `elements`, final `nodes`, and **`stages[]`**: index 0 = in-situ (u=0, geostatic σ per T3); 1…K = after each cut, with `element_stresses` for contouring. |
| `visualize.html` | Stage + displacement/stress dropdowns; parsed `sample.res` (log only — use JSON for σ). |

## Visualize `sample.res` and `sample.json`

After you have run the solver and produced `sample.res` and `sample.json`, open
`visualize.html` in a browser. Because of browser security, **auto-load of local
JSON** only works if you serve the folder over HTTP, or you can use the
**file pickers** on the page to select `sample.json` and `sample.res` from disk.

**Option A — local HTTP (recommended).** From the repository root (or this folder):

```powershell
cd samples\p610_t3
python -m http.server 8765
```

Then open `http://localhost:8765/visualize.html`. The page will try to
`fetch` `sample.json` and `sample.res` from the same directory.

**Option B — `file://`.** Open `visualize.html` by double-click, then use
**Load** → choose `sample.json` and `sample.res` in this folder.

Use the **Stage** dropdown: 0 = pre-excavation (in-situ stress, zero displacement),
then 1…K = after each excavation listed in the `.dat` file. Choose
**Displacement** (|u|, u<sub>x</sub>, u<sub>y</sub>) or **Stress** (centroid
σ<sub>xx</sub>, …, von Mises). Importing **only** `sample.res` cannot draw stress
fields; open the matching `sample.json` from the same run.

## Refreshing the sample

To regenerate `sample.dat` from the GUI, open the same project, use the
p610_t3 wizard to emit all sections, run the analysis once, then copy
`%TEMP%\GeotechSlopeApp_p610t3\p610t3_run.dat` back into this folder as
`sample.dat`.

To refresh the binary after C++ changes:

```powershell
cmake --build build --config Release --target p610_t3
Copy-Item -Force build\Release\p610_t3.exe samples\p610_t3\p610_t3.exe
```
