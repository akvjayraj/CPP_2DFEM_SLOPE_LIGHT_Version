# JR-04 results

RS3 Joint Verification Case 2 vs p610 T3 Goodman. Generated 2026-08-16.

Analytical: `u(y) = (γ/E)(H y − y²/2) + H(y − 1.5)·γ(H − 1.5)/kn`.

| Case | δc num (mm) | u_top num (m) | err_u % | err_F % | STAT |
| ---- | ----------: | ------------: | ------: | ------: | ---- |
| medium | 1.5000 | 0.02400 | 0.000 | 2.083 | PASS |
| coarse | 1.5000 | 0.02400 | 0.003 | 4.167 | PASS |
| fine | 1.5000 | 0.02400 | 0.001 | 1.042 | PASS |

Closure and `u(y)` match the Deb–Das body-force formula. Base `Fy` is a few percent low (reaction recovery); it is not the RS3 acceptance quantity. `err_F` halves each time the mesh is refined.
