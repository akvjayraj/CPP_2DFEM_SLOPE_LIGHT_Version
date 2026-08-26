# Phase 1: C++ Backend + X2D_GUI Integration

## Overview

The X2D_GUI WPF frontend is integrated with the C++ FEM backend via **process-based execution**:

1. **P51DatFileWriter** – Converts mesh/BCs/loads to p51 `.dat` format
2. **CppAnalysisRunner** – Runs `p51.exe` as a child process
3. **P51ResFileParser** – Parses `.res` output into C# models
4. **FEAnalysisViewModel** – Wires "Run FE Analysis" to the runner and populates ResultsViewModel

## Build Steps

### 1. Build C++ Backend

```powershell
cd cpp
.\build_cpp.bat
```

This produces `build/p51.exe` (and other programs).

### 2. Build X2D_GUI

```powershell
cd cpp
dotnet build X2D_GUI/GeotechSlopeApp.csproj
```

### 3. Run

**Option A – Run from cpp directory (recommended):**

```powershell
cd cpp
dotnet run --project X2D_GUI/GeotechSlopeApp.csproj
```

The app will search for `p51.exe` in `build/` relative to the working directory.

**Option B – Copy p51.exe next to the app:**

Copy `build/p51.exe` to `X2D_GUI/bin/Debug/net8.0-windows/` (or Release) so it runs alongside the GUI.

## Usage

1. Open the **FE Analysis** tab
2. Click **Run FE Analysis**
3. A default 2×2 rectangular mesh (p51 template) is analyzed
4. Results appear in **Results Visualization** tab (contours, vectors, data grids)

## Configuration

- **CppExecutableDirectory**: Set `FEAnalysisVM.CppExecutableDirectory` to the folder containing `p51.exe` if auto-detection fails
- **WorkingDirectory**: `CppAnalysisRunner.WorkingDirectory` – where `.dat`/`.res` files are written (default: temp)

## Data Flow

**p51 (2D FE):**
```
FEAnalysisViewModel.RunAnalysisCommand
    → P51DatFileWriter.Write()  →  p51_run.dat
    → Process.Start("p51.exe", "p51_run")
    → p51.exe reads .dat, writes p51_run.res
    → P51ResFileParser.Parse()  →  P51ParseResult
    → ResultsViewModel (FENodes, FEElements, FeElementStresses, etc.)
    → FEContourCanvasView, FEVectorCanvasView, DataGrids
```

**p41 (1D rod):**
```
Script tab: .dat content from P41DatContentBuilder (geometry, mesh, materials, loads, BCs)
    → Run p41 / Execute Script
    → CppAnalysisRunner.RunP41Async(ScriptContent)  →  writes p41_run.dat, runs p41.exe
    → p41.exe writes p41_run.res
    → P41ResFileParser.Parse()  →  P41ParseResult (nodes, rod elements, axial forces)
    → ResultsViewModel  →  Results & Visualization tab (displacements, mesh lines, element actions)
```

- **p41 executable**: Build the C++ project (e.g. `cmake --build build --config Debug`); a post-build step copies `p41.exe` from `build/Debug` (or `build/$(Configuration)`) into the GUI output so "Run p41" works without setting a path. If p41.exe is elsewhere, set `FEAnalysisVM.CppExecutableDirectory` to that folder (used by both p51 and p41).

## Where to add Analysis type and Element type

**Recommended: FE Analysis tab**

Add **Analysis type** and **Element type** on the **FE Analysis** tab (`Views/Tabs/FEAnalysisView.xaml`), so the user chooses physics and element before running.

- **Placement**: In the existing **"Analysis Options"** `GroupBox` (Grid.Row="1"), add two rows at the top:
  - **Analysis type**: `ComboBox` bound to a list from `programs/ANALYSIS_TYPES.md` (e.g. "Plane strain – elastic", "Steady seepage 2D", "Consolidation 4-node", "1D axial rod", …). Value maps to the first token in `.dat` (e.g. `plane_strain_elastic`, `seepage_2d`, `rod_1d`).
  - **Element type**: `ComboBox` bound to element options (e.g. "2-node rod", "4-node quad", "8-node quad"); filter by selected analysis type so only valid element types are shown (see `programs/PXX_BY_ELEMENT_TYPE.md`).
- **Use**: When writing `.dat` (P51DatFileWriter or a generic writer), set the first line from Analysis type and optionally the second from Element type; then run `fem_cpp.exe` (or the specific exe) with the dataname.
- **ViewModel**: Add properties and lists on `FEAnalysisViewModel` (e.g. `SelectedAnalysisType`, `SelectedElementType`, `AvailableAnalysisTypes`, `AvailableElementTypes`).

**Optional: Project Settings**

The **Project Settings** tab already has an **"Analysis Type"** sub-tab; that one is for **Deterministic vs Probabilistic** and **Dimension (1D/2D/3D)**, not FEM analysis/element. To set project-level defaults for the FE solver:

- Add a new sub-tab under Project Settings, e.g. **"FE solver"** or **"Default FE analysis"**, with Analysis type + Element type dropdowns; have the FE Analysis tab read these as initial values when the tab loads.

**Script tab (p41)**

The **Script** tab is currently fixed to p41 (1D rods). If you later support multiple analysis types from Script, either use the same Analysis/Element type from the FE Analysis tab (shared VM or project settings) or add a small Analysis type / Element type selector at the top of the Script tab.

## Analysis type ↔ Failure Criterion ↔ Material Type (Material Properties tab)

The **Material Properties** tab has **Failure Criterion** (Elastic, Elasto-Plastic, Mohr-Coulomb, Hoek-Brown, etc.) and **Material Type** (Isotropic, Transversely Isotropic, Orthotropic, etc.). These must be correlated with **Analysis type** and the C++ backend:

- **Elastic** → all elastic programs (p41–p47, p51–p57, eigenvalue, dynamics). Use E, ν (and density if needed).
- **Elasto-Plastic** → only **p45** (beam) and **p118** (2D Von Mises). Other analyses do not support plasticity in this backend.
- **Mohr-Coulomb / Hoek-Brown / Drucker-Prager** → not implemented in C++; keep parameters in the app for export or future use; warn or disable Run when selected.
- **Isotropic** → supported by all programs. **Transversely Isotropic / Orthotropic** → store in app; run as isotropic (E, ν) with a warning or use for export only.

See **X2D_GUI/ANALYSIS_MATERIAL_CORRELATION.md** for the full mapping, validation rules, and suggested GUI logic (filter Analysis type by Failure Criterion; warn when criterion or material type is not supported by the chosen analysis).

## Extending

- **Custom meshes**: Use `P51DatFileWriter.P51Input` with `FromRectangularMesh()` or build `P51Input` from MeshVM data
- **Staged excavation**: Run p51 per stage, merge results into `StagedAnalysisResult`
- **Other programs**: Add `RunP42Async`, `RunP43Async`, etc. with corresponding dat/res writers and parsers
