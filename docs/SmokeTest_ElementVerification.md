# 2D FEM Element Smoke Test Verification

| Field | Value |
|---|---|
| Document | SmokeTest_ElementVerification |
| Module | `p610_t3_elastic` (linear-elastic plane-strain solver) |
| Elements covered | T3, T6, Q4, Q8 |
| Convention | Tension-positive stress; SI units (kPa, m) |
| Last updated | 2026-05-02 |

> Purpose — Confirm that the `.dat` parser, C++ solver, GUI importer, and
> contour/visualization pipeline work end-to-end for each 2D element family,
> and compare FEM output against closed-form patch-test expectations where
> the geometry and BCs admit a clean analytical solution.

---

## 1. Executive Summary

### What these smoke tests prove

- The `.dat` input parser reads the chosen `mesh_type` (T3, T6, Q4, Q8) correctly.
- The solver assembles, solves, and writes the stress/displacement output.
- The GUI importer and contour renderer display the correct element family.
- For the Q4 patch test the FEM result agrees with the closed-form solution to machine precision.

### What these smoke tests do NOT prove

- They do not validate full geotechnical behavior (e.g., Kirsch stress redistribution, Mohr-Coulomb yielding, convergence).
- T6 and Q8 smoke cases use a loaded hypotenuse or a loaded half-edge, so the element stress is intentionally non-uniform.
- Mesh convergence, engineering tolerance, and benchmark validation require the full Kirsch / Salencon verification cases.

### Key takeaway

> Smoke tests confirm code paths. Kirsch and Salencon tests validate engineering physics. Always run a mesh convergence study before trusting production tunnel results.

---

## 2. Quick Interpretation Box

| If you see... | It probably means... |
|---|---|
| Q4 in `.dat` but a triangle in the contour view | Older GUI build; rebuild the GUI (fix applied 2026-05-02). |
| Node IDs shifted by 1 between `.dat` and the GUI | GUI is zero-based; `.dat` / JSON is one-based. Not an error. |
| Displacements display as `-0.000094` | Rounded fixed-decimal display of `-9.375e-5`. Raise precision to verify. |
| Stress is negative | Tension-positive convention; geotechnical compression is negative. |
| `σzz` non-zero with no z-load | Plane strain: `σzz = ν·(σxx + σyy)` is automatic. |
| All nodal displacements are zero | Viewing stage 0 (reference); select stage 1 (elastic solution). |
| Contour is flat / one colour | Single element with constant stress; expected for 1-element patch tests. |

---

## 3. Model Definition (common to all smoke tests)

### Material constants

| Property | Symbol | Value | Units |
|---|---|---|---|
| Young's modulus | E | 1,000,000 | kPa |
| Poisson ratio | ν | 0.25 | - |
| Unit weight | γ | 0 | kN/m³ |
| Constitutive law | - | linear elastic, isotropic | - |

### Loading convention

- Mode 0 direct stress on a specified edge: `sxx, syy, sxy` applied, and the solver computes the distributed edge traction as `T = σ · n` where `n` is the outward edge normal.
- Magnitude is 100 kPa compression (tension-positive convention renders this as -100).

### Kinematic constraints

- Fixed node at origin to remove translation.
- Rollers on axis edges to remove rotation and to enforce symmetry.

### Output convention

- Stresses: tension-positive, units of kPa.
- Displacements: meters.
- Plane strain: `σzz = ν·(σxx + σyy)`, `εzz = 0`.

---

## 4. Element Capability Matrix

| Feature | T3 | T6 | Q4 | Q8 |
|---|---|---|---|---|
| Corner nodes | 3 | 3 | 4 | 4 |
| Midside nodes | 0 | 3 | 0 | 4 |
| Total nodes | 3 | 6 | 4 | 8 |
| Shape-function order | Linear | Quadratic | Bilinear | Quadratic serendipity |
| Strain field | Constant (CST) | Linear | Linear | Quadratic |
| Typical integration | 1 Gauss pt | 3 pts | 2x2 pts | 2x2 / 3x3 pts |
| Recommended use | Coarse elastic, tunnels | Stress concentration | General 2D continuum | High-order stress accuracy |
| Known limitations | Stiff; averages stress | Needs midside defs | Volumetric locking risk | Sensitive to distortion |
| Smoke file shipped | via Kirsch quarter | `elastic_t6_smoke.dat` | `elastic_q4_smoke.dat` | `elastic_q8_smoke.dat` |

---

## 5. Sign Convention Panel

> Tension-positive: All reported stresses (`σxx`, `σyy`, `σzz`, `τxy`) use the continuum mechanics convention where tension is positive and compression is negative. A uniform 100 kPa compressive far-field stress is `σxx = -100 kPa` in the JSON result and in the contour legend.

Unit conversion

- 1 MPa = 1000 kPa
- 1 kPa = 0.001 MPa
- Default contour unit in the app is kPa.

---

## 6. Smoke Test Details

### 6.1 T3 Reference — Kirsch Quarter Model

No single-element T3 smoke file is shipped; the T3 pipeline is exercised by the full Kirsch quarter benchmark.

| Field | Value |
|---|---|
| Input `.dat` | `Verification/kirsch1/kirsch_quarter1.dat` |
| Element type | T3 |
| Nodes / Elements | 88 nodes, 140 elements |
| Geometry | Quarter annulus, a = 1 m, R = 10 m |
| Material | E = 10,000,000 kPa, ν = 0.2 |
| Loading | Isotropic far-field p = -30,000 kPa |
| Benchmark | Kirsch (1898) closed-form solution |
| Classification | Analytical benchmark |

Verification metrics are computed in the Verification tab by comparing FEM displacements and stresses against the Kirsch solution at wall and far-field sample points.

Pass when the wall radial displacement and far-field stresses match Kirsch within the user-selected tolerance (default 2 percent).

### 6.2 T6 Smoke — `elastic_t6_smoke`

Files

- Input: `Verification/kirsch1/elastic_t6_smoke.dat`
- Output: `Verification/kirsch1/elastic_t6_smoke.json`

Geometry

- Single 6-node triangle with corners `(0,0)`, `(1,0)`, `(0,1)` and midsides `(0.5,0)`, `(0.5,0.5)`, `(0,0.5)`.

Boundary conditions

| Node | Coord | Restraint |
|---|---|---|
| 1 | (0, 0) | Fixed (ux = uy = 0) |
| 2 | (1, 0) | Roller y (uy = 0) |
| 3 | (0, 1) | Roller x (ux = 0) |

Loading

- Mode 0 direct `(sxx = -100, syy = -100, sxy = 0)` on edge 2-3 (hypotenuse).
- Outward normal `n ≈ (1, 1)/√2`, giving traction `T ≈ (-70.71, -70.71) kN/m`.

FEM element stress (stage 1)

| Quantity | FEM value | Units |
|---|---|---|
| σxx | -100.000 | kPa |
| σyy | -100.000 | kPa |
| τxy | +33.333 | kPa |
| σzz | -50.000 | kPa |

FEM corner displacements

| Node | ux (m) | uy (m) |
|---|---|---|
| 1 | 0 | 0 |
| 2 | -1.875e-4 | 0 |
| 3 | 0 | -1.875e-4 |

Physical interpretation

- Only the hypotenuse is loaded; axis edges are traction-free rollers.
- The single reported element stress represents the T6 element average output, not a pointwise field.
- Plane-strain check: `σzz = ν·(σxx + σyy) = 0.25 · (-200) = -50 kPa`. Matches FEM.
- Classification: code-path acceptance check (not a uniform-stress patch test).

### 6.3 Q4 Smoke — `elastic_q4_smoke`

Files

- Input: `Verification/kirsch1/elastic_q4_smoke.dat`
- Output: `Verification/kirsch1/elastic_q4_smoke.json`

Geometry

- Single 4-node quadrilateral with corners `(0,0)`, `(1,0)`, `(1,1)`, `(0,1)`.

Boundary conditions

| Node | Coord | Restraint |
|---|---|---|
| 1 | (0, 0) | Fixed |
| 2 | (1, 0) | Roller y |
| 4 | (0, 1) | Roller x |

Loading

- Mode 0 direct `(sxx = -100, syy = -100, sxy = 0)` on edge 2-3 (right edge, normal `(1, 0)`).
- Effective edge traction `T = (-100, 0) kN/m` — uniform 100 kPa horizontal compression.

Analytical expected result

| Quantity | Formula | Analytical value | Units |
|---|---|---|---|
| σxx | Applied | -100.000 | kPa |
| σyy | 0 | 0.000 | kPa |
| τxy | 0 | 0.000 | kPa |
| σzz | ν·(σxx + σyy) | -25.000 | kPa |
| εxx | (1+ν)/E · [(1-ν)σxx - νσyy] | -9.375e-5 | - |
| εyy | (1+ν)/E · [(1-ν)σyy - νσxx] | +3.125e-5 | - |
| ux(x,y) | εxx · x | -9.375e-5 · x | m |
| uy(x,y) | εyy · y | +3.125e-5 · y | m |

FEM vs analytical — nodal displacements

| Node | FEM ux (m) | Analytical ux (m) | Δux | FEM uy (m) | Analytical uy (m) | Δuy |
|---|---|---|---|---|---|---|
| 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 2 | -9.3750e-5 | -9.3750e-5 | ~0 | 0 | 0 | 0 |
| 3 | -9.3750e-5 | -9.3750e-5 | ~0 | +3.1250e-5 | +3.1250e-5 | ~0 |
| 4 | 0 | 0 | 0 | +3.1250e-5 | +3.1250e-5 | ~0 |

FEM vs analytical — element stress

| Quantity | FEM | Analytical | Δ |
|---|---|---|---|
| σxx (kPa) | -100.000 | -100.000 | 0 |
| σyy (kPa) | 0.000 | 0.000 | 0 |
| τxy (kPa) | -5.4e-15 | 0 | ~0 (numerical) |
| σzz (kPa) | -25.000 | -25.000 | 0 |

Classification: analytical patch test — PASS to machine precision.

### 6.4 Q8 Smoke — `elastic_q8_smoke`

Files

- Input: `Verification/kirsch1/elastic_q8_smoke.dat`
- Output: `Verification/kirsch1/elastic_q8_smoke.json`

Geometry

- 8-node serendipity Q8 with corners `(0,0)`, `(1,0)`, `(1,1)`, `(0,1)` and midsides `(0.5, 0)`, `(1, 0.5)`, `(0.5, 1)`, `(0, 0.5)`.

Boundary conditions

| Node | Coord | Restraint |
|---|---|---|
| 1 | (0, 0) | Fixed |
| 2 | (1, 0) | Roller y |
| 4 | (0, 1) | Roller x |
| 8 | (0, 0.5) | Roller x |

Loading

- Mode 0 direct `(sxx = -100, syy = -100, sxy = 0)` on edge 2-6 (only the lower half of the right edge).

FEM result (stage 1)

| Quantity | FEM value | Units |
|---|---|---|
| σxx | -47.552 | kPa |
| σyy | -2.448 | kPa |
| τxy | +6.563 | kPa |
| σzz | -12.500 | kPa |
| ux(node 2) | -9.870e-5 | m |
| uy(node 4) | +3.464e-5 | m |

Physical interpretation

- Load on half the right edge creates a non-uniform stress state.
- The reported stress is a Gauss point value; it is not the element average.
- Plane-strain check: `σzz = 0.25 · (-47.552 - 2.448) = -12.500`. Matches FEM.
- Classification: code-path acceptance check — verifies the Q8 pipeline, not a uniform-stress patch test.

---

## 7. Comparative Summary Table

| Test | Element | Nodes | Geometry | Loading | Stress pattern | FEM σxx (kPa) | Benchmark type |
|---|---|---|---|---|---|---|---|
| Kirsch quarter | T3 | 88 nodes, 140 el | Quarter annulus | σ∞ = -30,000 kPa | Kirsch radial | varies with r | Analytical (Kirsch 1898) |
| T6 smoke | T6 | 6 | Right triangle | Hypotenuse -100 | Non-uniform | -100 | Code path |
| Q4 smoke | Q4 | 4 | Unit square | Right edge -100 | Uniform σxx | -100 | Analytical patch test (exact) |
| Q8 smoke | Q8 | 8 | Unit square | Half right edge -100 | Non-uniform | -47.55 (Gauss pt) | Code path |

---

## 8. Worked Q4 Example

Step 1 — Applied boundary loading

Mode 0 direct stress `(sxx = -100, syy = -100, sxy = 0)` is specified on edge 2-3 (from (1,0) to (1,1)). The solver builds the edge traction from the outward normal `n = (1, 0)`:

- `Tx = sxx · nx + sxy · ny = -100`
- `Ty = sxy · nx + syy · ny = 0`

Equivalent to a uniform 100 kPa horizontal compression on the right face.

Step 2 — Plane strain stress state

The remaining edges are traction-free rollers, and with the supports at the origin and along the axes, the patch test admits a uniform solution:

- σxx = -100 kPa, σyy = 0, τxy = 0.

Step 3 — Strain from compliance

With E = 1,000,000 kPa and ν = 0.25,

- εxx = (1+ν)/E · [(1-ν)·σxx - ν·σyy] = 1.25/1e6 · (0.75 · -100 - 0) = -9.375e-5.
- εyy = (1+ν)/E · [(1-ν)·σyy - ν·σxx] = 1.25/1e6 · (0 - 0.25 · -100) = +3.125e-5.

Step 4 — Nodal displacements by integration

- Node 2 (1, 0): ux = -9.375e-5, uy = 0.
- Node 3 (1, 1): ux = -9.375e-5, uy = +3.125e-5.
- Node 4 (0, 1): ux = 0, uy = +3.125e-5.

Step 5 — Plane strain σzz

`σzz = ν · (σxx + σyy) = 0.25 · -100 = -25 kPa`.

Result: FEM output from `elastic_q4_smoke.json` matches all five steps to machine precision.

---

## 9. Pass / Fail Criteria

Smoke test acceptance

- The solver executable completes with exit code 0.
- The JSON output is well formed (correct stage, node, and element counts).
- The GUI importer loads the mesh and renders the correct element family in the contour view.
- For patch tests (Q4): analytical and FEM values agree to machine precision.
- For non-uniform smoke cases (T6, Q8): FEM values reproduce the shipped reference JSON.

Analytical verification tolerance

- The Verification tab uses a user-selected tolerance (default 2 to 5 percent) against Kirsch / Salencon closed-form solutions.
- Smoke tests are not evaluated against that tolerance because their loading is intentionally non-uniform.

---

## 10. Troubleshooting

| Symptom | Cause | Resolution |
|---|---|---|
| Q4 contour looks triangular | Older GUI build ignored `n4` | Rebuild GUI after 2026-05-02 fix. |
| Node IDs do not match `.dat` | GUI is 0-based, `.dat` / JSON is 1-based | Apply `DAT(i) = GUI(i) + 1`. |
| Displacements show rounded | Default 6-decimal fixed display | Inspect the JSON directly for full precision. |
| Stress is negative | Tension-positive convention | Compression reads negative. |
| All nodal displacements zero | Stage 0 reference selected | Switch to stage 1 (elastic solution). |
| σzz non-zero with no z-load | Plane strain auto-computes σzz | Expected; check `σzz = ν · (σxx + σyy)`. |
| Stale results after code change | Cached JSON | Delete `.res` and `.json`, then re-run the solver. |
| Flat / one-colour contour | Single-element constant stress | Expected; refine the mesh to see gradients. |
| p610_t3_elastic.exe not found | Missing binary | Build the C++ project and ensure the exe is on the search path. |

---

## 11. Engineering Use Guidance

1. Smoke tests — confirm the full pipeline (parse → solve → export → import → visualize) for each element family.
2. Analytical benchmarks — use Kirsch (elastic) and Salencon (Mohr-Coulomb elasto-plastic) to validate physics.
3. Mesh convergence — for production runs, refine until displacement and stress at critical points stabilize.
4. Element choice — T6 / Q8 preferred near stress concentrations; T3 / Q4 acceptable for coarse or far-field regions; avoid distorted quadrilaterals.
5. Interpretation — cross-check outputs against hand calculations for simple geometries before trusting complex runs.

---

## 12. Result Review Checklist

Before trusting any p610 run, verify:

- Correct mesh type in `.dat` (`! mesh_type T3 | T6 | Q4 | Q8`).
- Element connectivity in the JSON (`n1`, `n2`, `n3`, and optional `n4`...`n8`).
- Material constants E, ν, γ match the intended problem.
- Boundary conditions match the intended physics.
- Load direction, magnitude, and unit system are correct.
- Stress sign convention (tension-positive) is understood.
- Correct stage is selected in the results viewer.
- Output type (σxx, σyy, |u|, ...) matches the quantity being reviewed.
- Node indexing mismatch (0-based GUI vs 1-based `.dat`) is accounted for.

---

## 13. Glossary

| Term | Meaning |
|---|---|
| T3 | 3-node linear triangle (CST) |
| T6 | 6-node quadratic triangle |
| Q4 | 4-node bilinear quadrilateral |
| Q8 | 8-node serendipity quadratic quadrilateral |
| CST | Constant Strain Triangle |
| Plane strain | 2D kinematic assumption with εzz = 0 |
| Traction | Surface stress vector, `T = σ · n` |
| Gauss point | Integration sampling point inside an element |
| Principal stress | Eigenvalue of the stress tensor |
| von Mises | Scalar stress measure for yielding |
| Tension-positive | σ > 0 is tension, σ < 0 is compression (continuum convention) |
| Compression-positive | σ > 0 is compression (geotechnical convention); NOT used in this solver output |

---

## 14. Analytical Formula Appendix

Plane strain compliance (tension-positive)

- `εxx = (1+ν)/E · [(1-ν)·σxx - ν·σyy]`
- `εyy = (1+ν)/E · [(1-ν)·σyy - ν·σxx]`
- `γxy = 2·(1+ν)/E · τxy`
- `σzz = ν·(σxx + σyy)`
- `εzz = 0`

Plane strain elasticity matrix (Voigt)

```
       E
D = ---------- * [[1-ν,  ν ,  0       ],
   (1+ν)(1-2ν)    [  ν , 1-ν,  0       ],
                  [  0 ,  0 , (1-2ν)/2]]
```

Consistent nodal edge traction

- `f_nodal = ∫_edge N^T · t dΓ`

---

## 15. Traceability

| Field | Value |
|---|---|
| Solver executable | `p610_t3_elastic.exe` |
| Solver source | `library/solver/p610_t3_elastic.cpp` |
| GUI importer | `X2D_GUI/Backend/P610T3JsonParser.cs` |
| GUI contour renderer | `X2D_GUI/Views/Controls/FEContourCanvasView.xaml.cs` |
| Analysis options schema | `X2D_GUI/Backend/P610AnalysisOptions.cs` |
| Smoke inputs | `Verification/kirsch1/elastic_{t6, q4, q8}_smoke.dat` |
| Smoke results | `Verification/kirsch1/elastic_{t6, q4, q8}_smoke.json` |
| T3 benchmark input | `Verification/kirsch1/kirsch_quarter1.dat` |
| Word generator | `docs/tools/build_smoke_help_docx.ps1` |
| Document last updated | 2026-05-02 |

---

End of document.
