# Groundwater overview

Groundwater pore pressures reduce effective stress (\(\sigma'=\sigma+u\)). Enable **Flow-Seepage/Groundwater** from **Project Settings → Analysis** (category **ON**), then pick a tree leaf. Refine geometry on the **Groundwater** tab.

## Water-coupling ladder (tree leaves)

| Leaf | Meaning | Engine |
|------|---------|--------|
| **Static** | **Prescribed pore pressure** from water tables, Ru, B-bar, or grids. Terzaghi effective stress in the mechanical deck | p610 / SRF with the groundwater block |
| **Steady State** | FE seepage computes heads, then \(u\). When SRF is also ON, **one-way seepage transfer** installs that \(u\) in the mechanical run | p73 (free-surface) or p74 (general mesh), then SRF if SRF is ON |
| **Transient State** | **Transient dissipation** of excess pore pressure with the θ-method. Not two-way solid–fluid coupling | p84 (structured Q4) or p88 (general mesh) |
| **Sequential** | Future: undrained increment → dissipate → update \(\sigma\) | Not in this release |
| **Fully coupled** | Future: two-way solid–fluid (Biot 1941 is theory only) | Not in this release |

## Pore-pressure methods (Static leaf)

| Method | Summary |
|--------|---------|
| **Water Table** | Phreatic surface; hydrostatic pressure below the table (see [Water tables](water-table.md)) |
| **Ru** | Pore pressure u = Ru · σᵥ (see [Ru, B-bar and grids](ru-bbar-grid.md)) |
| **B-bar** | u = B̄ · σᵥ style coefficient method |
| **Grid** | Interpolated total head, pressure head, or pore pressure from a scatter grid |

Piezometric lines are available as a related pore-pressure definition where enabled in Analysis Settings / Groundwater details.

## Where to edit what

| Task | Location |
|------|----------|
| Turn groundwater on/off; choose Static / Steady / Transient | Project Settings → Analysis Type → Flow-Seepage/Groundwater tree |
| Draw water tables / piezometric lines; edit grid points | Groundwater tab |
| Rapid drawdown surfaces and procedure | Analysis Type (visible when SRF is ON and Static is selected) |

> **Note:** Material saturated unit weights apply when saturation is enabled on the material; pore-pressure method selection still controls u along the slip surface.

## See also

- [Water tables and piezometric lines](water-table.md)
- [Ru, B-bar and pressure grids](ru-bbar-grid.md)
- [Analysis type](../project-settings/analysis-type.md)
- [Rapid drawdown](rapid-drawdown.md)
