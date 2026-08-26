# Axisymmetric elastic smoke (Q4 / T3)

Confirms S&G continuum axisymmetric kinematics in `*_SRF_axisym`:
`x = r`, `εθ = uᵣ/r`, Gauss weights × `r` (not literal `2πr`). Same convention as chap05/p51.

## Decks

| File | Role |
|------|------|
| `q4_radial_strip_axisym.dat` | One Q4, `r∈[1,2]`, `z∈[0,1]`, high `c`, `srf_F=1` |
| `q4_radial_strip_plane.dat` | Same mesh / materials, plane `*_SRF` twin |
| `t3_radial_strip_axisym.dat` | Two T3 patch; Core A `slope_stability_T3_SRF_axisym` smoke |

## Run

```bat
library\solver\SRF\slope_stability_Q4_SRF_axisym.exe q4_radial_strip_axisym
library\solver\SRF\slope_stability_Q4_SRF.exe q4_radial_strip_plane
library\solver\SRF\slope_stability_T3_SRF_axisym.exe t3_radial_strip_axisym
```

(cwd = this folder; pass basename without `.dat`)

## Observed (app baseline)

| Case | Result | Notes |
|------|--------|-------|
| Q4 axisym | max \|u\| ≈ 1.06×10⁻⁴ | `.res` logs `analysis_type axisymmetric` |
| Q4 plane | max \|u\| ≈ 9.90×10⁻⁵ | Same mesh; different Ke / body-force weight |
| T3 axisym | converges at F=1 | Core A path; `.res` logs axisymmetric |

Elastic (F_max=0 on Q4). Plane vs axisym displacements differ once hoop row and ×`r` are active.
