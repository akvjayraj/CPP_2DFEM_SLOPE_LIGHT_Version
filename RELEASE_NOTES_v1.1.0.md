# CPP_2DFEM_SLOPE v1.1.0

## Highlights

This release stabilizes **Strength Reduction Factor (SRF)** analysis for quad meshes, unifies solver routing under `library/solver/SRF/`, and improves FE Analysis run control and verification coverage.

## Features

### SRF solver routing (Q4 / Q8 / T3 / T6)
- **Q4 SRF** now routes to branded Core B executable `slope_stability_Q4_SRF` in `library/solver/SRF/` with explicit **p64** decks (no p610 mesh-type rewrite).
- **Q8 SRF** executables consolidated under `library/solver/SRF/` alongside Q4.
- **T3/T6 SRF** continue via Core A p610 path with literature-optimal routing (`SrfLiteratureOptimalRouting`).
- Removed deprecated **Q6 SRF** solver target and sources.

### Bracket search & trial execution
- New bracket search pipeline: `SlopeSrfBracketRunner`, `SlopeSrfTrialExecutor`, `SrfBracketSearchAlgorithm`.
- Explicit deck validation (`SrfExplicitDatValidator`) and freshness checks (`SrfExplicitDeckFreshness`) prevent stale iteration limits and mismatched mesh types.
- Trial convergence trusts `.res` displacement rows; improved bracket-failure reporting.

### GUI & analysis UX
- **Stop Analysis** button cancels in-flight solver runs; execution details no longer stick on "Running…" after failures.
- Default SRF iteration limit raised to **1000**; mesh-to-script auto-sync after Q4/Q8 mesh generation.
- BC-aware p64 tol/limit patching in `DatSolverControlExporter`.
- Boundary condition fixity mapping helper (`BoundaryConditionFixityMapper`).

### Mesh & tooling
- Pure quad and native Q8 popup meshers for structured/unstructured workflows.
- Q4/Q8 multi-trial sweep crash fix (4-node connectivity no longer rewritten to Q8).
- Solver linking documentation: `Verification/SOLVER_LINKING_FLOW.md`.

### Verification
- **Griffiths & Lane Example 1 (2H:1V)** projects for T3, T6, Q4, Q8 with GUI automation summary (Q4 FoS ≈ 1.481 on reference mesh).
- LEM 2H1V slope case, RS2 Example 01 T3 SRF comparison notes, Q4 SRF repro fixtures.
- GitHub Actions workflow for .NET unit tests (`.github/workflows/dotnet-tests.yml`).

## Build notes

Rebuild native solvers with CMake after pulling. Q4/Q8 SRF binaries deploy to `library/solver/SRF/`.

## Full changelog

See commits from `v1.0.0` to `v1.1.0` on the [main branch](https://github.com/akvjayraj/CPP_2DFEM_SLOPE/compare/v1.0.0...v1.1.0).
