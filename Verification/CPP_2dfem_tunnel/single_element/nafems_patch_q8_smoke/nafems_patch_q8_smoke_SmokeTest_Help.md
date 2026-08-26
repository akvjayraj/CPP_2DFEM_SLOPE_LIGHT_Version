# NAFEMS-style Patch Test: nafems_patch_q8_smoke

| Field | Value |
|---|---|
| Folder | Verification/nafems/nafems_patch_q8_smoke |
| Generated at | 2026-05-04 02:05:15 +05:30 |
| Commit | 4a6b63d |
| Tolerance (kPa) | 0.001000 |

## Closed-form Expectation

| Quantity | Expected (kPa) |
|---|---:|
| sxx | -47.5520833333333428 |
| syy | -2.44791666666666163 |
| txy | 6.56249999999999289 |
| szz | -12.5 |

## FEM Result Snapshot (worst element, Stage 1)

| Case | Mesh | Elements | sxx (kPa) | syy (kPa) | txy (kPa) | szz (kPa) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| `nafems_patch_q8_smoke` | Q8 | 1 | -47.552083 | -2.447917 | 6.562500 | -12.500000 | PASS |

## Acceptance Rules

- All elements must satisfy the closed-form within Tolerance (kPa).
- node_count, element_count, n_stages = 2 must be present in the solver JSON.
- Stage 1 element_stresses array must be finite for every element.
- For plane-strain elasticity szz = nu*(sxx + syy); the closed-form value above
  was pre-computed for the linear-elastic regime (the test pins the material to
  pseudo-elastic by raising cohesion / friction angle so plasticity never
  activates).

## Generated Artifacts

- nafems_patch_q8_smoke_SmokeTest_Summary.csv
- nafems_patch_q8_smoke_SmokeTest_Help.md
- nafems_patch_q8_smoke_SmokeTest_Help.docx
- nafems_patch_q8_smoke_SmokeTest_Help.pdf