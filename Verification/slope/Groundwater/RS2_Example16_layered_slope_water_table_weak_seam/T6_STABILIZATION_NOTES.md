# Example 16 — T3/T6 stabilization notes

## Why T3 is difficult here

- Constant-strain triangles (CST) lock under nearly incompressible plastic flow.
- 0.5 m base weak seam + hydrostatic water table create a thin, soft shear band.
- Wet SRF trials are **non-monotonic** if judged by iteration count alone:
  - F≈0.67 can hit the iteration limit with huge yield
  - F≈0.9 can finish with moderate yield
  - F≥1.0 can finish iters but with yield ≫ 25 (false stable)

## Mitigations implemented

1. **Yield-ratio gate** (`SlopeSrfTrialExecutor.MaxStableYieldRatio = 25`) rejects runaway plastic residual.
2. **Gravity increment floor** raised from 50 → **100** in `p610_t3_elastoplastic.cpp` for gravity-only layers.
3. UI automation uses `LoadIncrements = 100`, seed `0.95`, finer interface size `0.20`.
4. Wet FoS rebracketed from trial JSON meta via `rebracket_wet_yield_gate.ps1`.

## Current status (2026-07-19)

| Case | Result |
|------|--------|
| Wet yield-gate FoS | ≈ **0.95** (bracket [0.9, 1.0]) — **−6.9%** vs literature 1.02 |
| Dry F=0.5 | Converges (yield≈1.03, 12 iters) |
| Dry F=1.0 | **Does not converge** (1000 iters) on T3 |

Wet FoS is already inside the ±10% band. T6 is still recommended to get **dry F≥1** healthy and to tighten wet agreement toward 1.02.

## When to use T6

Prefer **T6** (`slope_stability_T6_SRF` / 6-node triangles) for the published verification claim if:

- Dry F=1 still fails the yield gate after gravity=100 (current T3 status), or
- A closer wet FoS than ≈0.95 is required for the report.

Suggested T6 mesh settings (GUI Mesh tab):

- Family: Triangles · 6-node
- Global size ≈ 2.0 m
- Near-boundary / interface ≈ 0.15–0.25 m
- Refine near material interfaces: on

Then re-run the same wet bracket with the yield gate.

## Geometry

Overlay check: open `geometry_overlay_rs2_example16.html` — outline, seam, cuts, and WT match the RS2 §16 reconstruction used in this repo.
