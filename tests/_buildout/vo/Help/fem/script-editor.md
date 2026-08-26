# Script Editor

The **Script Editor** tab has two languages:

| Mode | What you edit | Execute |
|------|----------------|---------|
| **Solver .dat** | p41 / p51 / p610 FEM input deck | Writes the deck and runs the matching C++ solver |
| **Python** | A notebook-style script | Runs CPython out-of-process; `project` drives the live GUI over a named-pipe JSON-RPC |

Python does **not** replace the solver deck. After `project.mesh.generate()` the GUI rebuilds `.dat`; `project.fe.run()` is the same path as **Run FE Analysis**.

## Python API

```python
# Simple homogeneous slope. Drives the live GUI; Run FE uses the generated .dat.
project.clear()
project.set_title("Python slope")
project.fe.set_ssr(True)

# Geometry (m): base 40, toe (40,10), crest (20,20)
project.geometry.set_outline([
    (0, 0), (40, 0), (40, 10), (20, 20), (0, 20),
])

# Material (Mohr–Coulomb)
project.materials.add("Clay", c_kpa=5, phi_deg=20, e_kpa=1e5, nu=0.3, gamma=20)

# Mesh
project.mesh.generate(element="Q4", size=2.0)

# BCs: left/right Roller X, base Roller Y, corners Fixed
project.bcs.apply_slope_defaults()

# Strength reduction
status = project.fe.run()
print(status)
print("srf", project.fe.srf())
```

`project.fe.set_ssr(True)` / `False` selects **Slope stability – strength reduction (SRF)** and turns that category ON or OFF. The Script Editor **SSR on** checkbox is the same switch. `project.fe.run()` raises if the active category is still OFF (it does not show the Project Settings dialog).

Requires CPython 3.11+. The Script Editor looks for `python` / `py` on PATH, then `%LocalAppData%\Programs\Python`, the registry, `tools\python-embed`, and `XFEM_PYTHON`. Use **Browse Python** to pick `python.exe` once. The client module is `scripts/xfem_client/xfem.py` (copied next to the app as `xfem_client/xfem.py`). The editor injects `from xfem import project` before your script runs.

## Solver .dat

- Open, edit and save `.dat` content for the active project
- Use the p610 wizard (when visible) to rebuild sections from geometry and materials
- The application chooses a solver from the failure criterion, analysis type and element family

## Tips

1. Keep a backup of hand-edited decks before regenerating from the GUI.
2. After a successful run, review the output pane; export FE results from the Results menu if needed.
3. For GUI-driven SRF you can stay on the FE Analysis tab; use Python when you want a repeatable notebook, or `.dat` when you need a hand-edited deck.

## See also

- [Finite element analysis](overview.md)
- [FE results](fe-results.md)
- [Projects and files](../getting-started/project-files.md)
