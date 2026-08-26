# Tunnel Benchmarks -- All Smoke Tests

| Field | Value |
|---|---|
| Root | `Verification/` |
| Suites | 11 |
| Generated at | 2026-05-05 15:51:36 +05:30 |
| Commit | 4a6b63d |

## Suite Dashboard

| Folder | Failure criterion | Variants | Verdict |
|---|---|---:|---|
| boundary_distance | Elastic | 12 | PASS |
| cc_mc_tunnel | MohrCoulomb | 4 | PASS |
| dp_vs_mc | DruckerPrager | 8 | PASS |
| hb_rock_tunnel | HoekBrown | 7 | PASS |
| kirsch_elastic | Elastic | 20 | PASS |
| lame_cylinder | Elastic | 4 | PASS |
| mesh_convergence | Elastic | 12 | PASS |
| peck_mair_settlement | ElastoPlastic | 4 | PASS |
| stress_rotation | Elastic | 16 | PASS |
| tunnel_near_slope | MohrCoulomb | 4 | PASS |
| twin_tunnel | MohrCoulomb | 8 | PASS |

## Source of Truth

Every `.dat` in this suite carries an `AnalysisOptions` header that declares its own analytical / FEM-delta / Peck-Mair expectations. Loading any such `.dat` via the GUI **Script tab -> Execute Script** auto-populates the **Verification** tab grid via `VerificationViewModel.TryAutoRunPatchComparison`.

## Regenerate

- `powershell -ExecutionPolicy Bypass -File tools/build_tunnel_benchmark_packages.ps1`
- Refresh master index: `powershell -ExecutionPolicy Bypass -File tools/run_all_verifications.ps1`