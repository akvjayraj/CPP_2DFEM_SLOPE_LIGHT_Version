# Example 16 — Why SRF did not match literature (~1.02)

## Literature

| Source | FoS |
|--------|----:|
| RS2 SSR | **1.02** |
| Bishop circular | 1.087 |
| Spencer circular | 1.093 |
| Spencer non-circular | 1.007 |

## App result after fix

| Quantity | Value |
|----------|------:|
| Yield-gate wet FoS | **≈ 0.950** (bracket [0.9, 1.0]) |
| Deviation vs 1.02 | **−6.9%** |
| Gate | `max_yield_ratio ≤ 25` |

## Root cause (not materials)

Deck φ/c/γ, weak seam, and phreatic surface **match** RS2 Part 1 §16.
See `geometry_overlay_rs2_example16.html`.

Mismatch came from:

1. **False-stable SRF trials** — wet F=1.0 finished under the iteration limit with yield≈48.
2. **Non-monotonic T3 CST response** with WT + thin seam when judged by iters alone.
3. **Stale Jul-10 automation** expanding downward to F=0.017 when seed failed.

## Fixes shipped

| Change | Location |
|--------|----------|
| Named yield-ratio gate + unit tests | `X2D_GUI/Backend/SlopeSrfTrialExecutor.cs`, `tests/.../SlopeSrfTrialYieldGateTests.cs` |
| Gravity floor 50 → 100 | `library/solver/p610_t3_elastoplastic.cpp` |
| Wet rebracket + refreshed summaries | `rebracket_wet_yield_gate.ps1`, `gui_automation/*` |
| Automation path / seed / incs | `scripts/RS2Example16UiAutomation/Program.cs` |
| Geometry overlay | `geometry_overlay_rs2_example16.html` |
| T6 guidance | `T6_STABILIZATION_NOTES.md` |
| Dry baseline runner | `run_dry_baseline.ps1` |

## How to refresh

```powershell
# Offline rebracket from existing trial JSON (fast)
.\rebracket_wet_yield_gate.ps1

# Optional live midpoint with rebuilt solver
.\rebracket_wet_yield_gate.ps1 -RunMissingTrials -SolverExe ..\..\..\..\build\Debug\p610_t3_elastoplastic.exe

# Dry F=1 after gravity fix
.\run_dry_baseline.ps1 -Factors 1.0 -SolverExe ..\..\..\..\build\Debug\p610_t3_elastoplastic.exe
```
