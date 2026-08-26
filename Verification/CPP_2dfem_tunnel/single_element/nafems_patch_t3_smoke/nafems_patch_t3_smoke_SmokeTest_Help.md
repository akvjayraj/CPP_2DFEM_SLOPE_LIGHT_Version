# NAFEMS-style Patch Test: nafems_patch_t3_smoke

| Field | Value |
|---|---|
| Folder | Verification/nafems/nafems_patch_t3_smoke |
| Generated at | 2026-05-04 02:05:15 +05:30 |
| Commit | 4a6b63d |
| Tolerance (kPa) | 0.001000 |

## Closed-form Expectation

| Quantity | Expected (kPa) |
|---|---:|
| sxx | -100 |
| syy | -100 |
| txy | 0 |
| szz | -50 |

## FEM Result Snapshot (worst element, Stage 1)

| Case | Mesh | Elements | sxx (kPa) | syy (kPa) | txy (kPa) | szz (kPa) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| `nafems_patch_t3_smoke` | T3 | 1 | -100.000000 | -100.000000 | 0.000000 | -50.000000 | PASS |

## Acceptance Rules

- All elements must satisfy the closed-form within Tolerance (kPa).
- node_count, element_count, n_stages = 2 must be present in the solver JSON.
- Stage 1 element_stresses array must be finite for every element.
- For plane-strain elasticity szz = nu*(sxx + syy); the closed-form value above
  was pre-computed for the linear-elastic regime (the test pins the material to
  pseudo-elastic by raising cohesion / friction angle so plasticity never
  activates).

## Generated Artifacts

- nafems_patch_t3_smoke_SmokeTest_Summary.csv
- nafems_patch_t3_smoke_SmokeTest_Help.md
- nafems_patch_t3_smoke_SmokeTest_Help.docx
- nafems_patch_t3_smoke_SmokeTest_Help.pdf