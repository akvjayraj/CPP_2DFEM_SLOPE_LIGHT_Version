# RS2 Example 1 T3 SRF Solver Comparison

Converted from the RS2 Example 1 p64/Q8 deck to a native T3 explicit mesh.

- Root solver: `C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\library\solver\slope_stability_T3_SRF.exe`
- SRF folder solver: `C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\library\solver\SRF\slope_stability_T3_SRF.exe`
- Geometry: `w1=20`, `s1=20`, `w2=10`, `h1=10`, `h2=5` m
- Material: `phi=19.6 deg`, `c=3 kPa`, `gamma=20 kN/m3`, `E=100000 kPa`, `nu=0.3`
- Mesh: native T3, `500` elements, `286` nodes

| F | root max disp | SRF max disp | abs diff | root yield | SRF yield | root iters | SRF iters | converged |
|---:|---:|---:|---:|---:|---:|---:|---:|:---:|
| 0.5 | 1.699928E-002 | 1.699928E-002 | 0.000E+000 | 6.860963E-001 | 6.860963E-001 | 2 | 2 | True |
| 0.8 | 1.699928E-002 | 1.699928E-002 | 0.000E+000 | 9.787503E-001 | 9.787503E-001 | 2 | 2 | True |
| 0.9 | 1.699912E-002 | 1.699912E-002 | 0.000E+000 | 9.998587E-001 | 9.998587E-001 | 4 | 4 | True |
| 0.95 | 1.699929E-002 | 1.699929E-002 | 0.000E+000 | 9.999953E-001 | 9.999953E-001 | 5 | 5 | True |
| 0.99 | 1.699928E-002 | 1.699928E-002 | 0.000E+000 | 9.999835E-001 | 9.999835E-001 | 5 | 5 | True |
| 1 | 1.699962E-002 | 1.699962E-002 | 0.000E+000 | 9.999998E-001 | 9.999998E-001 | 5 | 5 | True |
| 1.05 | 1.699928E-002 | 1.699928E-002 | 0.000E+000 | 9.999906E-001 | 9.999906E-001 | 5 | 5 | True |
| 1.1 | 1.699928E-002 | 1.699928E-002 | 0.000E+000 | 9.999847E-001 | 9.999847E-001 | 5 | 5 | True |
| 1.2 | 1.700022E-002 | 1.700022E-002 | 0.000E+000 | 9.999951E-001 | 9.999951E-001 | 5 | 5 | True |

Maximum absolute displacement difference between solver locations: `0.000000E+000`.
Result: no numerical difference was detected between the two solver locations for this converted T3 case.
