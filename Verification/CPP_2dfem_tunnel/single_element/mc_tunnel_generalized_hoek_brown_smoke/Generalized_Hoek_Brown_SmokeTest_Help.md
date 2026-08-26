# Generalized Hoek Brown Smoke Test Help

| Field | Value |
|---|---|
| Folder | Verification/mc_tunnel/mc_tunnel_generalized_hoek_brown_smoke |
| Failure criterion | GeneralizedHoekBrown |
| Material type | Isotropic |
| Solver executable | p610_t3_generalized_hoek_brown |
| Elements covered | T3, T6, Q4, Q8 |
| Generated at | 2026-05-04 23:54:44 +05:30 |
| Commit | 4a6b63d |

## Executive Summary

This folder contains professional smoke tests for Generalized Hoek Brown only. The tests validate parser, solver, staged result writing, and visualization import paths for T3/T6/Q4/Q8.

These are software acceptance tests, not full tunnel benchmark validation.

## FEM Result Snapshot (Stage 1)

| Case | Mesh | Nodes | Stages | sxx (kPa) | syy (kPa) | szz (kPa) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| mc_tunnel_generalized_hoek_brown_q4_smoke | Q4 | 4 | 2 | -100.000000 | 0.000000 | -25.000000 | PASS |
| mc_tunnel_generalized_hoek_brown_q8_smoke | Q8 | 8 | 2 | -47.552083 | -2.447917 | -12.500000 | PASS_WITH_INTERPRETATION |
| mc_tunnel_generalized_hoek_brown_t3_smoke | T3 | 3 | 2 | -100.000000 | -100.000000 | -50.000000 | PASS |
| mc_tunnel_generalized_hoek_brown_t6_smoke | T6 | 6 | 2 | -100.000000 | -100.000000 | -50.000000 | PASS |

## Acceptance Rules

- node_count matches mesh type (3/6/4/8).
- element_count = 1 and n_stages = 2.
- Stage 1 stresses and displacements are finite.
- Plane-strain consistency check: szz = nu_eff * (sxx + syy). nu_eff is parsed from column 6 of the .dat material row and adjusted with the same scaling the C++ effective_elastic_constants helper applies: TransverselyIsotropic x 0.95, Orthotropic x 0.90, ViscoElastic x 0.98; all other laws use the raw nu0. The result is clamped to the open interval 0.05 to 0.49.
- Q8 is treated as PASS_WITH_INTERPRETATION when finite and plane-strain-consistent.

## Command Cookbook

- cd Verification\mc_tunnel\mc_tunnel_generalized_hoek_brown_smoke
- ..\..\..\build\Release\p610_t3_generalized_hoek_brown.exe mc_tunnel_generalized_hoek_brown_t3_smoke
- ..\..\..\build\Release\p610_t3_generalized_hoek_brown.exe mc_tunnel_generalized_hoek_brown_t6_smoke
- ..\..\..\build\Release\p610_t3_generalized_hoek_brown.exe mc_tunnel_generalized_hoek_brown_q4_smoke
- ..\..\..\build\Release\p610_t3_generalized_hoek_brown.exe mc_tunnel_generalized_hoek_brown_q8_smoke

## Generated Artifacts

- Generalized_Hoek_Brown_SmokeTest_Summary.csv
- Generalized_Hoek_Brown_SmokeTest_Help.md
- Generalized_Hoek_Brown_SmokeTest_Help.docx
- Generalized_Hoek_Brown_SmokeTest_Help.pdf