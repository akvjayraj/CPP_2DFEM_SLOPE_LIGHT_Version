# Sensitivity, probabilistic, reports, and export

Wave 3 is the delivery layer. Run a strength-reduction analysis first so these tools have a baseline SRF.

## Sensitivity and parametric

**Sensitivity & Probabilistic → Sensitivity Analysis** perturbs c, φ, γ, and E one at a time by the chosen percentage and ranks the change in SRF. **Parametric Analysis** sweeps one parameter. **Export Tornado CSV** writes the table.

## Probabilistic

1. Enable **Project Settings → Analysis Type → Probabilistic**.
2. On each material (**Add/Edit Material → Probabilistic**), enable variation and mark c, φ, and/or γ as random variables.
3. Choose Latin Hypercube, Monte Carlo, or Point Estimate, then **Run Probabilistic**.

Results report mean SRF, σ, P(SRF &lt; design), and reliability index β.

## Back analysis

Finds a strength factor *F* so SRF(c/*F*, atan(tan φ / *F*)) matches the target FoS. **Apply to Materials** writes the back-figured strengths into the library.

## Analysis cases

Named cases store k_h, k_v, and a load factor. **Apply Selected** writes seismic coefficients into Load Conditions. **Evaluate All** scales the last SRF for each active case. **Capture Current SRF** stores the last solved value on the selected case.

Construction staging (excavate / fill per stage) remains on **Project Settings → Analysis Type** and the stage footer.

## Reports and interchange

- **Report Generation** builds Markdown and PDF (letterhead, materials, groundwater, validation, sensitivity, reliability).
- **File → Export → Results** writes nodal/element CSV or VTK.
- **File → Export → Abaqus / ANSYS** writes `.inp` / `.cdb` from the current mesh (or last FE result).

Evaluations that do not re-run the native solver use a first-order scale of the last SRF (Mohr–Coulomb strength, load factor, and 1+|k_h|). Re-run FE Analysis when you need a full bracket search for a changed case.
