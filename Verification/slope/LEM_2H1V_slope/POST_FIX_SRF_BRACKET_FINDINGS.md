# LEM_2H1V_slope — SRF bracket search after the excavation-layer fix

## Context

This note records a direct re-run of the `slope_stability_T3_SRF` solver for
`lem_slope.xfem` **after** applying the two fixes from
`fix_spurious_excavation_layer_bug` (spurious excavation-layer fallback in
`P610T3DatContentBuilder.BuildExcavationLayers`/`BuildLayersBody`, and the
SRF-subfolder solver routing fix in `CppAnalysisRunner.ResolveSourceSolverExecutablePath`).

The project's saved `.dat` (`script.scriptContent` in `lem_slope.xfem`) was
patched by hand to match the exact shape the fixed C# code now generates
(`SolverControl` line `... 1` instead of `... 2`, and a single
`! Excavation layer 1 — 0 elements` / `0` block instead of the old
2-layer block with a fabricated 1-element excavation) — this shape was
independently confirmed against the fixed code via
`tests/GeotechSlopeApp.Tests.Core/P610T3LayersRoutingTests.cs`. Everything
else (mesh, materials, BCs) is untouched real project data.

Geometry/material match the reference LEM document exactly: toe (10,10),
crest (25,17.5)–(35,17.5), left/right boundaries (0,0)-(0,10) / (35,0)-(35,17.5),
`phi=20°`, `c=20 kPa`, `gamma=20 kN/m³`, Mohr–Coulomb. LEM methods (Bishop,
Spencer, Janbu, Morgenstern-Price, etc., per `lem (3).docx`) give **FoS ≈ 1.7–2.2**
for this problem.

## Bracket search result (post-fix)

Ran the corrected `.dat` through `library\solver\SRF\slope_stability_T3_SRF.exe`
directly for `srf_F` from 1.0 up to 50.0 and inspected each trial's
`meta.srf` block in the `.json` output:

| F        | max_yield_ratio | max_disp (m) | converged |
|---------:|----------------:|--------------:|:---------:|
| 1.0 – 9.0 | ~0.99–1.00      | 2.72 (flat)   | true      |
| 9.2       | ~4.8e4          | ~5.7e100 (blow-up) | true (spuriously) |
| 9.4 – 50  | 1e4–1.7e5       | ~1e100+ (blow-up) | true (spuriously) |

**The transition from a stable, converged solve to numerical blow-up happens
between F = 9.0 and F = 9.2** — i.e. the post-fix critical FoS for this
project is **≈ 9.0–9.2**, not the ≈1.7–2.2 given by every LEM method in the
reference document.

## Interpretation

The excavation-layer fix (root cause 1) is a genuine, necessary correctness
fix — it removes a spurious excavated element that was forcing an unrelated
staged-excavation code path — and it did move the blow-up point down from the
pre-fix ≈12–15 to ≈9.0–9.2. **However, it does not close the gap with LEM.**
A ~4–5× FEM-vs-LEM ratio is far outside the 10–20% tolerance that holds for
the other verified cases in this repo (e.g. `RS2_Example01_simple_slope`), so
**an additional, separate root cause is still present** for this specific
project. Candidates worth investigating next (not part of the
`fix_spurious_excavation_layer_bug` plan and not addressed here):

- `E = 1000 kPa` is unusually soft for this material (two orders of magnitude
  softer than the verified RS2 T3 deck's `E = 100000 kPa`); combined with the
  gravity-only layer's 20-increment self-weight ramp, this produces a ~2.72 m
  "geostatic" displacement that stays essentially constant from F=1 through
  F=9 before the solve suddenly diverges — this pattern (flat, converged,
  small plastic strain right up until a sudden jump to non-physical values)
  is more consistent with a numerical-conditioning/locking artifact than a
  genuine, gradually-developing shear-failure mechanism.
- The `max_yield_ratio ≈ 1.0` from `F=1.0` onward likely reflects a
  localized stress concentration (e.g. at the toe or a fixed-BC corner) hitting
  yield immediately, rather than a progressive global mechanism — worth
  checking element-by-element yield distribution, not just the single max
  value.
- K0/geostatic initialization behavior specific to the `gravity_only_layer`
  path for this mesh/BC combination.
- T3 mesh quality/coarseness near the toe (locking is a known T3 weakness).

Because of this open gap, **no `expected_FoS`/`expected_FoS_tolerance_pct`
verification entry was added** for this project (the plan listed this as
optional) — recording FoS≈9 as "expected" would misleadingly bless an
unresolved discrepancy, and the LEM value can't be asserted as "expected"
either since the current solver doesn't reach it.

## Reproduction

The exact `.dat` used (`lem_slope_fixed_base.dat`, with `! srf_F <value>`
substituted per trial) and raw trial outputs were generated under a local
`repro/` scratch folder during this investigation and were not committed
(regenerate by patching `script.scriptContent` from `lem_slope.xfem` per the
description above, or by loading the project in the GUI once it emits the
corrected `.dat` shape and running the SRF bracket search).
