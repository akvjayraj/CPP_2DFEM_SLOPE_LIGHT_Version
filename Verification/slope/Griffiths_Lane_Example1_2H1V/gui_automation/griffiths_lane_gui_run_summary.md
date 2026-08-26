# Griffiths & Lane (1999) Example 1 GUI Automation Summary

Generated: `2026-07-05T04:16:21.2473612+05:30`
Literature FoS reference: `1.40`

| Mesh | Branded exe | Process exe | Core | FoS | Deviation | Deviation % |
|---|---|---|---|---:|---:|---:|
| T3 | slope_stability_T3_SRF | slope_stability_T3_SRF | CoreA | 1.481 | 0.081 | 5.788 |
| T6 | slope_stability_T6_SRF | slope_stability_T6_SRF | CoreA | 1.428 | 0.028 | 1.986 |
| Q4 | slope_stability_Q4_SRF | slope_stability_Q8_SRF | CoreB | 1.481 | 0.081 | 5.792 |
| Q8 | slope_stability_Q8_SRF | slope_stability_Q8_SRF | CoreB | 1.352 | -0.048 | -3.460 |

## T3

Project: `C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\Verification\slope\Griffiths_Lane_Example1_2H1V\griffiths_lane_example1_2h1v_t3.xfem`
Status: `Critical FoS ≈ 1.481 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.477, 1.484])`
FoS note: Critical FoS ≈ 1.481 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.477, 1.484])

| SRF | Max displacement | Iterations | Converged |
|---:|---:|---:|:---:|
| 1.0000 | 0.0338409 | 55 | Yes |
| 1.2500 | 0.0398114 | 52 | Yes |
| 1.3750 | 0.0436345 | 132 | Yes |
| 1.4375 | 0.0461443 | 409 | Yes |
| 1.4688 | 0.0533878 | 837 | Yes |
| 1.4766 | 0.0564152 | 929 | Yes |
| 1.4844 | 0.0598843 | 1000 | No |
| 1.5000 | 0.0693778 | 1000 | No |

## T6

Project: `C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\Verification\slope\Griffiths_Lane_Example1_2H1V\griffiths_lane_example1_2h1v_t6.xfem`
Status: `Critical FoS ≈ 1.428 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.422, 1.43])`
FoS note: Critical FoS ≈ 1.428 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.422, 1.43])

| SRF | Max displacement | Iterations | Converged |
|---:|---:|---:|:---:|
| 1.0000 | 0.0337067 | 59 | Yes |
| 1.2500 | 0.0399231 | 74 | Yes |
| 1.3750 | 0.0436525 | 216 | Yes |
| 1.4062 | 0.0449377 | 601 | Yes |
| 1.4219 | 0.0482285 | 890 | Yes |
| 1.4297 | 0.0507284 | 1000 | No |
| 1.4375 | 0.0537624 | 1000 | No |
| 1.5000 | 0.0547644 | 1000 | No |

## Q4

Project: `C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\Verification\slope\Griffiths_Lane_Example1_2H1V\griffiths_lane_example1_2h1v_Q4.xfem`
Status: `Critical FoS ≈ 1.481 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.477, 1.484])`
FoS note: Critical FoS ≈ 1.481 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.477, 1.484])

| SRF | Max displacement | Iterations | Converged |
|---:|---:|---:|:---:|
| 1.0000 | 0.033828 | 14 | Yes |
| 1.2500 | 0.0396631 | 20 | Yes |
| 1.3750 | 0.0426954 | 88 | Yes |
| 1.4375 | 0.044763 | 228 | Yes |
| 1.4688 | 0.0484252 | 368 | Yes |
| 1.4766 | 0.049797 | 395 | Yes |
| 1.4844 | 0.0513862 | 1000 | No |
| 1.5000 | 0.0551691 | 1000 | No |

## Q8

Project: `C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\Verification\slope\Griffiths_Lane_Example1_2H1V\griffiths_lane_example1_2h1v_Q8.xfem`
Status: `Critical FoS ≈ 1.352 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.344, 1.352])`
FoS note: Critical FoS ≈ 1.352 (auto-detected bracket in 2 expansion + 6 bisection trial(s), F ∈ [1.344, 1.352])

| SRF | Max displacement | Iterations | Converged |
|---:|---:|---:|:---:|
| 1.0000 | 0.0338151 | 24 | Yes |
| 1.2500 | 0.0396381 | 48 | Yes |
| 1.3125 | 0.0412863 | 141 | Yes |
| 1.3438 | 0.042297 | 879 | Yes |
| 1.3516 | 0.044345 | 1000 | No |
| 1.3594 | 0.049906 | 1000 | No |
| 1.3750 | 0.0661382 | 1000 | No |
| 1.5000 | 0.29486 | 1000 | No |
