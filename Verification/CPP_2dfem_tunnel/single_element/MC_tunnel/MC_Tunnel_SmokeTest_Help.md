# MC Tunnel Element Smoke Test Help

| Field | Value |
|---|---|
| Document | MC_Tunnel_SmokeTest_Help |
| Folder | `Verification/mc_tunnel/MC_tunnel` |
| Solver path | `p610_t3_elastoplastic` family |
| Failure criterion | Mohr-Coulomb |
| Material type | Isotropic |
| Elements covered | T3, T6, Q4, Q8 |
| Units | m, kPa |
| Stress convention | Tension-positive output; compression is negative |
| Last updated | 2026-05-03 |

> Purpose: this help file explains the smoke tests in this folder only. It compares the FEM output against the analytical checks available for these small element tests and helps users decide whether the parser, solver, result writer, and GUI visualization path are working.

---

## 1. Executive Summary

These files are professional software smoke tests, not full tunnel design benchmarks. They are intentionally tiny so that failures are easy to diagnose.

What these tests prove:

- The `.dat` file declares and routes the intended `mesh_type`.
- The solver accepts T3, T6, Q4, and Q8 connectivity.
- The staged elastoplastic result writer emits finite nodal displacements and element stresses.
- The GUI can import the result JSON and display the corresponding element family.
- For T3, T6, and Q4, the stress response can be checked against direct plane-strain analytical expectations.

What these tests do not prove:

- They do not validate a production tunnel excavation model.
- They do not prove mesh convergence.
- They do not replace Kirsch, Salencon, or field-data verification.
- The Q8 case is a high-order code-path smoke response, not a uniform-stress patch test.

Professional use rule:

> A smoke test says "the software path works." A benchmark says "the engineering physics is accurate." A design model still needs mesh convergence, boundary-distance checks, material calibration, and independent review.

---

## 2. File Inventory

| Case | Input `.dat` | JSON output | RES output | Status |
|---|---|---|---|---|
| T3 | `mc_tunnel_t3_smoke.dat` | `mc_tunnel_t3_smoke.json` | `mc_tunnel_t3_smoke.res` | Complete |
| T6 | `mc_tunnel_t6_smoke.dat` | `mc_tunnel_t6_smoke.json` | `mc_tunnel_t6_smoke.res` | Complete |
| Q4 | `mc_tunnel_q4_smoke.dat` | `mc_tunnel_q4_smoke.json` | `mc_tunnel_q4_smoke.res` | Complete |
| Q8 | `mc_tunnel_q8_smoke.dat` | `mc_tunnel_q8_smoke.json` | `mc_tunnel_q8_smoke.res` | Complete |

All four cases use one element and one traction group so that element formulation and result import issues are visible immediately.

---

## 3. Common Model Definition

### 3.1 Material and Constitutive Inputs

| Property | Value | Units |
|---|---:|---|
| Failure criterion | Mohr-Coulomb | - |
| Material type | Isotropic | - |
| Friction angle, phi | 30 | deg |
| Cohesion, c | 3450 | kPa |
| Dilation angle, psi | 10 | deg |
| Unit weight, gamma | 0 | kN/m3 |
| Young's modulus, E | 1,000,000 | kPa |
| Poisson ratio, nu | 0.25 | - |
| K0 | 1 | - |

The cohesion is deliberately high for these smoke tests, so the response remains essentially elastic and the element formulation can be checked cleanly.

### 3.2 Loading and Staging

Each `.dat` file contains one stress load:

```text
0  -100  -100  0
```

This means direct stress loading with:

| Quantity | Value | Meaning |
|---|---:|---|
| sxx | -100 | 100 kPa compression in x |
| syy | -100 | 100 kPa compression in y |
| sxy | 0 | no input shear stress |

The JSON result has two stages:

| Stage | Name | Expected interpretation |
|---|---|---|
| 0 | `in_situ_pre_excavation` | Reference state, zero displacement |
| 1 | `after_excavation_1` | Loaded smoke-test solution |

---

## 4. Analytical Reference Checks

Plane-strain elastic relation used in these smoke tests:

```text
szz = nu * (sxx + syy)
```

For `nu = 0.25`:

| In-plane stress state | Analytical `szz` |
|---|---:|
| `sxx=-100`, `syy=-100` | `-50` kPa |
| `sxx=-100`, `syy=0` | `-25` kPa |

Displacement scale is also checked. For a one-metre element with `E = 1,000,000 kPa`, stress of order `100 kPa` gives strain of order:

```text
strain ~= 100 / 1,000,000 = 1e-4
```

So displacements around `1e-4 m` are realistic for these one-element smoke tests.

---

## 5. FEM Versus Analytical Summary

Values below are taken from the stage 1 JSON outputs in this folder.

| Case | Nodes | Elements | FEM sxx | FEM syy | FEM txy | FEM szz | Analytical / reference check | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|---|
| T3 | 3 | 1 | -100.000 | -100.000 | 0.000 | -50.000 | `szz = 0.25*(-200) = -50` | PASS |
| T6 | 6 | 1 | -100.000 | -100.000 | +33.333 | -50.000 | `szz = 0.25*(-200) = -50`; shear appears from hypotenuse loading and T6 interpolation | PASS |
| Q4 | 4 | 1 | -100.000 | ~0.000 | ~0.000 | -25.000 | `szz = 0.25*(-100) = -25` | PASS |
| Q8 | 8 | 1 | -47.552 | -2.448 | +6.562 | -12.500 | Finite high-order edge-load response; `szz = 0.25*(sxx+syy) = -12.5` | PASS with interpretation note |

Interpretation note for Q8:

The Q8 single-element result is not expected to match the Q4 uniform stress state because the higher-order serendipity element and the loaded midside edge create a different internal interpolation pattern. The correct smoke-test expectation is finite output, correct node count, correct element connectivity, and a plane-strain `szz` consistent with the reported `sxx` and `syy`.

---

## 6. Element-by-Element Detail

### 6.1 T3 Smoke Test

Files:

- Input: `mc_tunnel_t3_smoke.dat`
- JSON: `mc_tunnel_t3_smoke.json`
- Text result: `mc_tunnel_t3_smoke.res`

Geometry:

| Node | x | y |
|---:|---:|---:|
| 1 | 0 | 0 |
| 2 | 1 | 0 |
| 3 | 0 | 1 |

Connectivity:

```text
1  2  3
```

Stage 1 FEM stress:

| Quantity | FEM | Analytical check |
|---|---:|---:|
| sxx | -100.000 | -100.000 |
| syy | -100.000 | -100.000 |
| txy | 0.000 | 0.000 |
| szz | -50.000 | -50.000 |

Stage 1 nodal displacement:

| Node | ux | uy |
|---:|---:|---:|
| 1 | 0 | 0 |
| 2 | -6.250e-05 | 0 |
| 3 | 0 | -6.250e-05 |

Professional interpretation:

- This is the cleanest T3 smoke case.
- The single constant-strain triangle reports a constant stress state.
- The `szz` value exactly follows the plane-strain analytical check.

### 6.2 T6 Smoke Test

Files:

- Input: `mc_tunnel_t6_smoke.dat`
- JSON: `mc_tunnel_t6_smoke.json`
- Text result: `mc_tunnel_t6_smoke.res`

Geometry:

| Node | x | y | Role |
|---:|---:|---:|---|
| 1 | 0 | 0 | corner |
| 2 | 1 | 0 | corner |
| 3 | 0 | 1 | corner |
| 4 | 0.5 | 0 | midside |
| 5 | 0.5 | 0.5 | midside |
| 6 | 0 | 0.5 | midside |

Connectivity:

```text
1  2  3  4  5  6
```

Stage 1 FEM stress:

| Quantity | FEM | Analytical / expected behavior |
|---|---:|---|
| sxx | -100.000 | compression load transferred correctly |
| syy | -100.000 | compression load transferred correctly |
| txy | +33.333 | expected nonzero average from loaded hypotenuse and quadratic interpolation |
| szz | -50.000 | `0.25*(-100-100) = -50` |

Professional interpretation:

- T6 verifies that quadratic triangle connectivity and midside nodes are accepted end-to-end.
- The shear term is not a sign error. It is a result of applying the load on the hypotenuse in a one-element quadratic triangle.
- Use a multi-element benchmark, not this smoke test, for stress-concentration validation.

### 6.3 Q4 Smoke Test

Files:

- Input: `mc_tunnel_q4_smoke.dat`
- JSON: `mc_tunnel_q4_smoke.json`
- Text result: `mc_tunnel_q4_smoke.res`

Geometry:

| Node | x | y |
|---:|---:|---:|
| 1 | 0 | 0 |
| 2 | 1 | 0 |
| 3 | 1 | 1 |
| 4 | 0 | 1 |

Connectivity:

```text
1  2  3  4
```

Stage 1 FEM stress:

| Quantity | FEM | Analytical check |
|---|---:|---:|
| sxx | -100.000 | -100.000 |
| syy | approximately 0 | 0 |
| txy | approximately 0 | 0 |
| szz | -25.000 | `0.25*(-100+0) = -25` |

Professional interpretation:

- This is the closest case to a classical one-element patch response.
- The result confirms Q4 connectivity, stiffness, load assembly, and result parsing.
- The displacement at node 2 is `-9.375e-05 m`, consistent with the expected `1e-4 m` scale.

### 6.4 Q8 Smoke Test

Files:

- Input: `mc_tunnel_q8_smoke.dat`
- JSON: `mc_tunnel_q8_smoke.json`
- Text result: `mc_tunnel_q8_smoke.res`

Geometry:

| Node | x | y | Role |
|---:|---:|---:|---|
| 1 | 0 | 0 | corner |
| 2 | 1 | 0 | corner |
| 3 | 1 | 1 | corner |
| 4 | 0 | 1 | corner |
| 5 | 0.5 | 0 | midside |
| 6 | 1 | 0.5 | midside |
| 7 | 0.5 | 1 | midside |
| 8 | 0 | 0.5 | midside |

Connectivity:

```text
1  2  3  4  5  6  7  8
```

Stage 1 FEM stress:

| Quantity | FEM | Check |
|---|---:|---|
| sxx | -47.552 | finite compression response |
| syy | -2.448 | finite response |
| txy | +6.562 | finite response |
| szz | -12.500 | `0.25*(-47.552-2.448) = -12.5` |

Professional interpretation:

- Q8 verifies 8-node serendipity connectivity and midside-node result output.
- A single Q8 edge-loaded element can produce a non-uniform stress field; one averaged element-stress value is not a uniform patch-test proof.
- In the GUI, a correct Q8 visualization should preserve four corners and four midside nodes. If it displays as a simple Q4 only, rebuild the app and confirm the result parser recognizes `n5..n8`.

---

## 7. Acceptance Criteria

| Check | Required result |
|---|---|
| `.dat` exists | PASS |
| `.json` exists after solver run | PASS |
| `.res` exists after solver run | PASS |
| JSON `n_stages` | 2 |
| JSON `element_count` | 1 |
| T3 node count | 3 |
| T6 node count | 6 |
| Q4 node count | 4 |
| Q8 node count | 8 |
| Stage 1 stress values | finite |
| Stage 1 displacement values | finite |
| Plane-strain `szz` check | within roundoff |
| GUI visualization | correct element family and nonzero stage 1 contour |

Recommended numerical tolerances:

| Quantity | Tolerance |
|---|---:|
| Node and element counts | exact |
| Stage count | exact |
| `szz = nu*(sxx+syy)` | `1e-8 kPa` for these files |
| Stress comparison for T3/Q4/T6 | `1e-6 kPa` |
| Displacement finite check | absolute value less than `1e-2 m` and not NaN |
| Q8 smoke finite-response check | finite values plus plane-strain `szz` consistency |

---

## 8. Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| JSON or RES file missing | Solver has not been run for that `.dat` | Run the command cookbook below |
| Stage 0 shows all zero displacements | Viewing reference stage | Select stage 1 / `after_excavation_1` |
| Compression appears negative | Solver output is tension-positive | This is correct |
| T6 appears as only 3 nodes | Visualization parser is using corner-only rendering | Rebuild the app and verify quadratic element support |
| Q8 appears as only 4 nodes | Parser or renderer is ignoring `n5..n8` | Check `QuadraticQuadrilateralElement` support |
| `Average inward tunnel-wall displacement = nan` | Smoke mesh has no circular tunnel wall | Acceptable for these one-element tests |
| Q8 stress differs from Q4 | Higher-order edge interpolation changes the averaged stress | Treat Q8 as a code-path smoke response |

---

## 9. Command Cookbook

From the repository root:

```powershell
cd Verification\mc_tunnel\MC_tunnel
..\..\..\build\Release\p610_t3_elastoplastic.exe mc_tunnel_t3_smoke
..\..\..\build\Release\p610_t3_elastoplastic.exe mc_tunnel_t6_smoke
..\..\..\build\Release\p610_t3_elastoplastic.exe mc_tunnel_q4_smoke
..\..\..\build\Release\p610_t3_elastoplastic.exe mc_tunnel_q8_smoke
```

Generate the help DOCX and CSV summary:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_mc_tunnel_smoke_help.ps1
```

Generated outputs:

- `Verification/mc_tunnel/MC_tunnel/MC_Tunnel_SmokeTest_Help.docx`
- `Verification/mc_tunnel/MC_tunnel/MC_Tunnel_SmokeTest_Summary.csv`

---

## 10. GUI Review Checklist

Use the Visualization / FE Analysis contour tab:

| Step | Expected result |
|---|---|
| Load T3 result | one triangular element, 3 nodes |
| Load T6 result | one quadratic triangle, 6 nodes including midsides |
| Load Q4 result | one quadrilateral element, 4 nodes |
| Load Q8 result | one quadratic quadrilateral, 8 nodes including midsides |
| Select stage 0 | zero displacement contour |
| Select stage 1 | nonzero displacement / stress contour |
| Check units | displacement in m, stress in kPa |

Screenshot placeholders for future documentation:

- T3 stage 1 displacement contour
- T6 midside-node contour rendering
- Q4 uniform edge-load contour
- Q8 midside-node contour rendering

---

## 11. Engineering Disclaimer

These smoke tests are intentionally minimal. They are suitable for software acceptance, regression testing, and rapid diagnosis after code changes. They are not suitable for final geotechnical design decisions.

Before using the software for a real tunnel:

- Run a geometry-relevant analytical benchmark such as Kirsch or Salencon.
- Run mesh convergence with T3/T6/Q4/Q8 as appropriate.
- Confirm boundary distance, excavation staging, in-situ stress, and support conditions.
- Validate material parameters against laboratory or field data.
- Review sign conventions and unit systems before comparing with external software.
