# Q4-Q8 SRF solvers

Core B unified geotechnical drivers for quadrilateral slope-stability workflows.

| Program family | Examples |
|----------------|----------|
| Unified geotechnical | `fem_cpp.exe`, `p64.exe` |

T3/T6/Q4/Q8 branded SRF exes live in `../SRF/`.
p610 structural-static exes live in `../Structural-Static Elasticity/`.

CMake `deploy_solver_to_q4q8_folder()` syncs `fem_cpp` and `p64` Release builds here.

The GUI resolves these via `CppAnalysisRunner.GetPreferredSolverSubfolder()`.
