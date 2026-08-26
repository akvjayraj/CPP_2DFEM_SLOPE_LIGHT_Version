# Ex2 pseudo-static verification (two-layer Q8 SRF)

## Decks

| Deck | Path | k_h | k_v |
|------|------|-----|-----|
| Baseline | `Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat` | 0 | 0 |
| Seismic | `Verification/slope/Ex2_twolayerslope_seismic/ex2_twolayer_slope_seismic.dat` | 0.1 | 0.05 |

Both use `fem_cpp` → `p64` → `slope stability_Q8_SRF.cpp`.

## Sign convention

- Horizontal inertia: **+x**, magnitude `k_h × γ × element weight` (lumped to nodes).
- Vertical gravity term: `−(1 − k_v) × γ × weight` on **y** (Smith tension-positive load vector).
- JSON `pseudo_static_nodal_forces` lists the **pseudo-static-only** components (`k_h` horizontal; `−k_v` vertical), separate from total gravity.

## Automated check

```powershell
cmake --build build --config Release --target fem_cpp
.\tools\verify_ex2_pseudo_static.ps1
```

Asserts:

1. Seismic `.res` contains `Pseudo-static coefficients: k_h=0.1  k_v=0.05`
2. Seismic `.json` has `pseudo_static` and non-empty `pseudo_static_nodal_forces`
3. Seismic `max|u|` from `.res` SRF table is **greater than** baseline (warning if not)

## GUI E2E

1. Load Ex2 seismic project or set Seismic / Dynamic stage with k_h/k_v.
2. Script Editor shows `! k_h` / `! k_v` under `! <BEGIN:AnalysisOptions>`.
3. Run FE Analysis → Results → **Pseudo-static Seismic Loads** table and Visualization → orange force vectors.
