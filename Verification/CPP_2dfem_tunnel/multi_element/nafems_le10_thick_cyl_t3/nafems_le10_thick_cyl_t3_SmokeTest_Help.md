# NAFEMS-style Patch Test: nafems_le10_thick_cyl_t3

| Field | Value |
|---|---|
| Folder | Verification/nafems/nafems_le10_thick_cyl_t3 |
| Generated at | 2026-05-04 02:05:15 +05:30 |
| Commit | 4a6b63d |
| Tolerance (kPa) | 25.000000 |

## Closed-form Expectation

| Quantity | Expected (kPa) |
|---|---:|
| sxx |  |
| syy |  |
| txy |  |
| szz |  |

## FEM Result Snapshot (worst element, Stage 1)

| Case | Mesh | Elements | sxx (kPa) | syy (kPa) | txy (kPa) | szz (kPa) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| `nafems_le10_thick_cyl_t3` | T3 | 192 | 81.596430 | -40.171438 | -92.552403 | 10.356248 | PASS |

## Acceptance Rules

- All elements must satisfy the closed-form within Tolerance (kPa).
- node_count, element_count, n_stages = 2 must be present in the solver JSON.
- Stage 1 element_stresses array must be finite for every element.
- For plane-strain elasticity szz = nu*(sxx + syy); the closed-form value above
  was pre-computed for the linear-elastic regime (the test pins the material to
  pseudo-elastic by raising cohesion / friction angle so plasticity never
  activates).

## Generated Artifacts

- nafems_le10_thick_cyl_t3_SmokeTest_Summary.csv
- nafems_le10_thick_cyl_t3_SmokeTest_Help.md
- nafems_le10_thick_cyl_t3_SmokeTest_Help.docx
- nafems_le10_thick_cyl_t3_SmokeTest_Help.pdf