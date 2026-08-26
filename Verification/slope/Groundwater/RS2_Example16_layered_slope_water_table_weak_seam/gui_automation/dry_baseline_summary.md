# Example 16 dry baseline

Generated: `2026-07-19` (classified with yield gate ≤ 25 from existing trial JSON)

Solver artifacts: `gui_automation/t3/p610t3_script_run_srf_*_dry.json`  
Gravity floor in current solver: **100** increments (`p610_t3_elastoplastic.cpp`) — re-run via `run_dry_baseline.ps1` for overnight confirmation.

| F | GW | Converged (gate) | Yield | Iters | max\|u\| | File |
|---:|:---:|:---:|---:|---:|---:|---|
| 0.5 | off | **true** | 1.026 | 12 | 0.229 | `p610t3_script_run_srf_0_5_dry.json` |
| 1.0 | off | **false** | 1.000 | 1000 | 0.069 | `p610t3_script_run_srf_1_dry.json` |

## Conclusion

- Dry F=0.5 is stable under the yield-ratio gate.
- **Dry F=1 does not converge** (hits iteration limit) on this T3 mesh — even without groundwater.
- Therefore the model is numerically stiff before WT is applied; wet FoS≈0.95 (yield-gate) is credible only as a T3 engineering estimate.
- Prefer **T6** (see `T6_STABILIZATION_NOTES.md`) before claiming dry FoS ≥ 1 or tightening wet agreement beyond ±10% of literature 1.02.

## Next overnight command

```powershell
.\run_dry_baseline.ps1 -Factors 1.0,0.75,0.5 `
  -SolverExe ..\..\..\..\build\Debug\p610_t3_elastoplastic.exe
```
