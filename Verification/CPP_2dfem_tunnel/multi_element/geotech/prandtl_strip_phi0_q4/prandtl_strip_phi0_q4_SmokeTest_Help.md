# Geotech Bearing-Capacity Test: prandtl_strip_phi0_q4

| Field | Value |
|---|---|
| Folder | Verification/geotech/prandtl_strip_phi0_q4 |
| Generated at | 2026-05-05 02:37:46 +05:30 |
| Commit | 4a6b63d |
| Tolerance (% of p_applied) | 5.000000 |

## Closed-form Expectation

| Quantity | Value |
|---|---:|
| phi (deg) | 0 |
| c (kPa) | 10 |
| q_ult / c (Nc) | 5.14159265 |
| p_applied (kPa) | 51.4159265 |

## FEM Result Snapshot (final stage)

| Case | Mesh | Elements | min(syy) (kPa) | load-xfer err (%) | yield ratio | Verdict |
|---|---:|---:|---:|---:|---:|---|
| `prandtl_strip_phi0_q4` | Q4 | 120 | -51.734081 | 0.618786 | 0.997485 | PASS |

## Acceptance Rules

- Every load increment must converge (max iters < limit).
- min(syy) must equal -p_applied within Tolerance% (load-transfer check).
- max yield ratio = max_e (s1-s2)/[2*(c*cos(phi) - sigma_m*sin(phi))] must
  reach >= 0.90 (Mohr-Coulomb yield essentially saturated at q_ult).
- A run that satisfies load-transfer + convergence but not yield-saturation
  is reported as PASS_WITH_INTERPRETATION (sub-collapse loading).

## Generated Artifacts

- prandtl_strip_phi0_q4_SmokeTest_Summary.csv
- prandtl_strip_phi0_q4_SmokeTest_Help.md
- prandtl_strip_phi0_q4_SmokeTest_Help.docx
- prandtl_strip_phi0_q4_SmokeTest_Help.pdf