# S&G Program 7.3 — Unconfined / free-surface flow (p73)

Verification project for **Flow-Seepage/Groundwater → Steady State → Unconfined / free-surface flow**.

## Files

| File | Purpose |
|------|---------|
| `sg_p73_unconfined_free_surface.xfem` | GUI project |
| `reference_sg_p73_1.dat` | Classic Smith & Griffiths `chap07/p73_1.dat` |
| `expected_gui_p73_deck.dat` | Deck matching this geometry / heads (GUI BuildP73 layout) |

## Model

- Domain: rectangle `0 ≤ x ≤ 8`, `0 ≤ y ≤ 8` (SI)
- Mesh: structured **8×7 Q4** (72 nodes, 56 elements) — required by p73
- Material: `kx = ky = 0.001`
- Heads (Groundwater → Water Head, UserDefined):
  - **Upstream** `H = 7` (max-x face in p73 convention)
  - **Downstream** `H = 2` (min-x face)
- Free-surface iteration: tol `0.01`, max iters `20`

## How to run

1. Open the `.xfem` in the app.
2. Confirm Flow-Seepage/Groundwater is ON and Steady State → **Unconfined / free-surface flow** (+ Water Head).
3. FE Analysis → Run (`fem_cpp` **p73**).
4. Expect iterative free-surface convergence; results: total head / pressure head / flow.

## Notes

- p73 needs a **structured rectangular Q4** mesh (solver moves node elevations while iterating).
- Irregular slopes → use **General 2D (p74)** or **Static** WT instead.
- Classic `p73_1` smoke: converged in ~10 iters with inflow ≈ outflow ≈ 0.0028.
