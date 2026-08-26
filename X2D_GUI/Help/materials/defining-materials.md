# Defining materials

Materials live on the **Material Properties** tab and in the project’s material library. Each material has identity fields, stiffness (for FE), unit weights, strength parameters, and optional hydraulic properties.

## Creating and editing

Use the Material Properties menu or tab commands:

| Action | Purpose |
|--------|---------|
| **Add Material...** | Create a new named material |
| **Edit Selected Material...** | Open the material dialog for the selection |
| **Delete Selected Material** | Remove from the library |
| **Duplicate Material** | Copy parameters to a new entry |
| **Material Library...** | Browse / manage library entries |
| **Import / Export** | Exchange material databases |

Typical identity fields: name, description, display colour, failure criterion, and material type (isotropic and related FE constitutive types).

## Parameters used by FEM

For finite-element work, the essential inputs are:

- Unit weight (and optional saturated unit weight)
- Stiffness (Young’s modulus / Poisson’s ratio, or the equivalent for the chosen constitutive model)
- Strength model and its parameters (for example Mohr–Coulomb cohesion and friction angle for SRF)
- Optional hydraulic properties when seepage is enabled

Stiffness and permeability drive continuum and seepage response; strength parameters control yield and strength reduction.

## Probabilistic variation

When probabilistic analysis is enabled, a material can expose random variables (means, distributions, cohesion–φ correlation, and other property correlations). Configure statistics on the material and the Analysis Settings Statistics / Random Numbers pages.

## See also

- [Layers and assignment](layer-assignment.md)
- [Finite element analysis](../fem/overview.md)
