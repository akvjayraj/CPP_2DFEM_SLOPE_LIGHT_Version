# NAFEMS-style Patch Test: nafems_le10_thick_cyl_q4

| Field | Value |
|---|---|
| Folder | Verification/nafems/nafems_le10_thick_cyl_q4 |
| Generated at | 2026-05-04 02:05:15 +05:30 |
| Commit | 4a6b63d |
| Tolerance (kPa) | 5.000000 |

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
| `nafems_le10_thick_cyl_q4` | Q4 | 96 | -83.587209 | 150.437937 | -15.404997 | 16.712682 | PASS |

## Acceptance Rules

- All elements must satisfy the closed-form within Tolerance (kPa).
- node_count, element_count, n_stages = 2 must be present in the solver JSON.
- Stage 1 element_stresses array must be finite for every element.
- For plane-strain elasticity szz = nu*(sxx + syy); the closed-form value above
  was pre-computed for the linear-elastic regime (the test pins the material to
  pseudo-elastic by raising cohesion / friction angle so plasticity never
  activates).

## Generated Artifacts

- nafems_le10_thick_cyl_q4_SmokeTest_Summary.csv
- nafems_le10_thick_cyl_q4_SmokeTest_Help.md
- nafems_le10_thick_cyl_q4_SmokeTest_Help.docx
- nafems_le10_thick_cyl_q4_SmokeTest_Help.pdf