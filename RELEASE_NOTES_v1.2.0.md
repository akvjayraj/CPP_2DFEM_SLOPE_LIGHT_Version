# CPP_2DFEM_SLOPE v1.2.0

## Highlights

This release focuses on **correct SRF results with seismic and groundwater**, fixes T3 strength-reduction failure detection, and expands groundwater / Project Settings UX plus verification coverage.

## Features

### Seismic coefficients preserved through SRF trials
- `! k_h` / `! k_v` are first-class fields on `P610AnalysisOptions` (parse + emit in AnalysisOptions).
- SRF trial patches (`TryPatchSrfFSingleTrial`, bracket settings, native SRF prepare, material option merges) use record `with` clones so pseudo-static lines are **not stripped** before `slope_stability_T3_SRF` runs.
- Fixes the regression where seismic FoS matched the dry (non-seismic) result.

### Run FE Analysis keeps seismic enabled
- `GetScriptContentForSolverRun` no longer imports a mesh-stripped `.dat` that clears the **Enable seismic analysis** checkbox mid-run.
- When seismic is on in the GUI, the GUI is the source of truth and coefficients are re-patched into the run deck.

### T3 SRF failure classification
- Disables dry “yield settled” force-final shortcut under SRF so collapse can hit the iteration limit.
- Allows viscoplastic flow under SRF when mean stress goes tensile (collapse path).
- Marks trials with extreme yield ratio as failed; GUI treats high `max_yield_ratio` / `all_increments_converged=false` as failed bracket trials.

### Groundwater & seepage
- Hydrostatic / grid pore-pressure path in the T3 solver with effective-stress yield checks.
- Groundwater GUI (surfaces, Ru, water-pressure grids) and seepage dat/json builders.
- FE Overview shows groundwater and **Loads (Load Conditions)** summaries with magnitudes.

### Project Settings & analysis UX
- Split Project Settings tabs (General / Analysis Type / Grid & Axes).
- Boundary-condition canvas symbols and analysis-category gating.
- Water-pressure grid dialog and persistence.

### Verification & tests
- RS2 Example 01 / 02 / 14 / 16 and Arai–Tagyo dry/wet groundwater projects.
- Unit tests: `TryPatchSrfFSingleTrial` and `PrepareDatForNativeSlopeSrfExecutable` preserve `k_h`/`k_v`.
- UI automation helpers for RS2 / Griffiths–Lane cases.

## Build notes

Rebuild native solvers with CMake after pulling (`slope_stability_T3_SRF` and related SRF targets under `library/solver/`). Rebuild the WPF GUI (Release) so AnalysisOptions Kh/Kv preservation and seismic run sync are active.

## Full changelog

See commits from `v1.1.0` to `v1.2.0` on the [main branch](https://github.com/akvjayraj/CPP_2DFEM_SLOPE/compare/v1.1.0...v1.2.0).
