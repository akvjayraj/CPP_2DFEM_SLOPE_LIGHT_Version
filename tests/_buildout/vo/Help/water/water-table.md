# Water tables and piezometric lines

When the groundwater method is **Water Table** (or a piezometric definition is active), pore pressures are taken from a phreatic or piezometric surface drawn in model coordinates.

## Water table

1. Enable **Groundwater** on Analysis Settings → Groundwater.
2. Set Method to **Water Table**.
3. On the **Groundwater** tab, draw or edit the water-table polyline across the slope.
4. Confirm the line spans the region where slip surfaces will be evaluated.

Below the water table, pore pressure at a point on the slip surface is computed from the hydrostatic head to the phreatic surface (subject to the solver’s water-table implementation and any ponded-water options present in the project).

> **Tip:** Keep water-table vertices monotonic in a practical left-to-right order so interpolation along slices remains stable.

## Piezometric lines

Piezometric lines provide an alternate pressure reference when the pore-pressure method is set to the piezometric option in Analysis Settings. Use them when measured heads do not coincide with a single free water table.

## Ponded water

Where ponded-water overlays are defined, reservoir or ditch water above the ground surface can contribute external water loads and boundary heads consistent with the groundwater settings.

## Checks before analysis

- Water table intersects the soil domain where needed.
- Materials that should use saturated unit weight have saturation enabled.
- Axis limits still show the full water surface.

## See also

- [Groundwater overview](groundwater-overview.md)
- [Ru, B-bar and pressure grids](ru-bbar-grid.md)
- [Rapid drawdown](rapid-drawdown.md)
