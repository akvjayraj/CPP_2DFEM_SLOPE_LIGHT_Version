# Joint verification

Goodman interface (conforming joint) checks. Each case is a loadable `.xfem` plus notes.

| Case | Joint | Folder |
| ---- | ----- | ------ |
| JR-01 | Horizontal, normal compression | [JR01_SingleJointNormalCompression](JR01_SingleJointNormalCompression/) |
| JR-02 | 45° to horizontal, vertical compression | [JR02_SingleJoint45DegCompression](JR02_SingleJoint45DegCompression/) |
| JR-03 | RS3 Case 1: 3 m column, axial pressure, `u(y)` | [JR03_SingleJointColumnAxial](JR03_SingleJointColumnAxial/) |
| JR-04 | RS3 Case 2: same column, body force `γ = 10` MPa/m | [JR04_SingleJointColumnBodyForce](JR04_SingleJointColumnBodyForce/) |
| JR-05 | RS3 Case 4: triaxial MC slip vs joint angle | [JR05_TriaxialJointSlip](JR05_TriaxialJointSlip/) |

Open the `.xfem` in the GUI. Click Mesh (T3) before Run FE so the joint line becomes a split interface.

Numerical vs analytical comparison: [joint_verification_results.txt](joint_verification_results.txt). Refresh with `tools/compare_to_analytical.ps1`.
