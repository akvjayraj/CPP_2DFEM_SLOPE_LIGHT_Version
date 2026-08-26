# JR-03 results

RS3 Joint Verification Case 1 vs p610 T3 Goodman. Generated 2026-08-16.

Analytical: `u(y) = P y / E + H(y − 1.5) · (P / kn)`.

At `P = 1 MPa`: joint jump `0.100 mm`, top displacement `1.600 mm`.

| Case | δc num (mm) | u_top num (mm) | err_u % | err_F % | STAT |
| ---- | ----------: | -------------: | ------: | ------: | ---- |
| medium 1 MPa | 0.1000 | 1.5998 | 0.009 | 0.000 | PASS |
| medium 2 MPa | 0.2000 | 3.1997 | 0.009 | 0.000 | PASS |
| coarse 1 MPa | 0.1000 | 1.5998 | 0.009 | 0.000 | PASS |
| fine 1 MPa | 0.1000 | 1.5998 | 0.009 | 0.000 | PASS |

Displacement profile at `x = 0.5` (medium, 1 MPa):

| y (m) | u_an (mm) | u_num (mm) | err % |
| ----: | --------: | ---------: | ----: |
| 0.00 | 0.0000 | 0.0000 | 0.000 |
| 0.75 | 0.3750 | 0.3750 | 0.010 |
| 1.50− | 0.7500 | 0.7499 | 0.010 |
| 1.50+ | 0.8500 | 0.8499 | 0.009 |
| 2.25 | 1.2250 | 1.2249 | 0.009 |
| 3.00 | 1.6000 | 1.5998 | 0.009 |

The 0.01% shortfall vs the 1D bar formula is the plane-strain Poisson effect at `ν = 0.01`. RS2/RS3 plot the same `u(y)` (top ≈ 1.6 mm).
