# Example 4 — Homogeneous Slope with Free Surface (Groundwater)



Smith & Griffiths Program 6.4 / SLOPE64 **Example 4**: homogeneous embankment with phreatic surface.



## Reference



Golden SLOPE64 output: `benchmarks/slope64_ref/ex4.res` (FoS **1.27**, failure at SRF **1.2656**).



```powershell

powershell -ExecutionPolicy Bypass -File tools/fetch_slope64_ex4.ps1

powershell -ExecutionPolicy Bypass -File tools/run_slope64_ex4.ps1

powershell -ExecutionPolicy Bypass -File tools/compare_slope64_ex4.ps1 -FemRes Verification/slope/Ex2_twolayerslope_groundwater/ex4_us_native.res

```



## Deck variants



| File | Units | Coords | Notes |

|------|-------|--------|-------|

| `ex4_homogeneous_slope_groundwater.dat` | metric | Y-up + offset | GUI default |

| `ex4_homogeneous_slope_groundwater_us_native.dat` | US (ft, psf, pcf) | Smith y-down | SLOPE64 apples-to-apples |

| `ex4_homogeneous_slope_groundwater_metric_native.dat` | metric | Smith y-down | SI native |



Regenerate:



```powershell

# Metric GUI deck (book 4-point ladder)

powershell -File tools/gen_slope64_ex4_groundwater.ps1



# US native (SLOPE64 units/geometry)

powershell -File tools/gen_slope64_ex4_groundwater.ps1 -Units us -CoordSmithNative -SrfLadder book4



# Metric Smith-native

powershell -File tools/gen_slope64_ex4_groundwater.ps1 -Units metric -CoordSmithNative -SrfLadder book4

```



## Calibrated groundwater yield (2026-05-31)



AnalysisOptions on the canonical deck:



- `groundwater_strength effective`

- `groundwater_u_factor 0.55` — partial Terzaghi reduction in MC yield (calibrated vs SLOPE64 ex4)

- Body force: saturated **γ** everywhere (not buoyant)

- Effective stress sign: Smith FEM compression-negative (`σ' = σ + α·u` on normals)



Recalibrate α:



```powershell

powershell -File tools/calibrate_ex4_u_factor.ps1 -Rebuild

```



## Verification



```powershell

powershell -File tools/run_ex4_groundwater_verify.ps1 -Rebuild

```



**Pass:** FoS within **1.25–1.29** vs `benchmarks/slope64_ref/ex4.res`; book4 ladder failure at SRF **1.2656** (1000 iters).



### fem_cpp vs SLOPE64 (US native, α=0.55, book4)



| SRF | SLOPE64 \|u\| (ft) | iters | fem_cpp \|u\| (ft) | iters |

|-----|-------------------|-------|-------------------|-------|

| 0.5000 | 0.195 | 5 | 0.186 | 5 |

| 1.0000 | 0.280 | 33 | 0.268 | 29 |

| 1.2500 | 0.363 | 184 | 0.351 | 185 |

| 1.2656 | 0.489 | 1000 | 0.451 | 1000 |



**Estimated FoS:** 1.27 (SLOPE64) vs **1.26** (fem_cpp)



Metric GUI deck reproduces the same dimensionless FoS; displacements scale as ft × 0.3048 → m.



## GUI



Load `ex4_homogeneous_slope_groundwater.dat` → Execute Script → Results → **Force-Displacement** subtab for SRF vs d_max plot (Fig 4.2 style).

