# Analysis type

The **Analysis Type** page under Project Settings keeps a **category tree** on the left and a **leaf detail panel** on the right. Turn a category **ON**, then pick a leaf. Run FE Analysis follows that ON + leaf combination.

A compact header stays visible above the tree:

| Control | Role |
|---------|------|
| **Idealization** | Plane strain or axisymmetric continuum (same persist field as mechanical SRF axisymmetry) |
| **Engine** | Read-only summary of the solver path that Run FE will use |

Deterministic / Probabilistic sits under the tree as a one-line footer. Problem dimension (1D / 2D / 3D) remains below.

## Flow-Seepage leaves (water-coupling ladder)

Do not treat p84 / p88 as two-way solid–fluid coupling. The live leaves are:

| Tree leaf | Coupling | Engine |
|-----------|----------|--------|
| **Static** | Prescribed pore pressure (WT / Ru / B-bar / grid) → Terzaghi \(\sigma'=\sigma+u\) | Mechanical / SRF deck with the groundwater block |
| **Steady State** | FE seepage heads → \(u\); optional **one-way seepage transfer** into SRF when SRF is also ON | p73 (free-surface) or p74 (general mesh), then SRF if SRF is ON |
| **Transient State** | **Transient dissipation**: θ-method excess \(u(t)\) | p84 (structured Q4) or p88 (general mesh) |
| **Sequential** *(disabled)* | Future: undrained increment → dissipate → update \(\sigma\) | Not in this release |
| **Fully coupled** *(disabled)* | Future: two-way solid–fluid (Biot 1941 is theory only) | Not in this release |

Rapid drawdown options appear only when **SRF is ON** and the **Static** leaf is selected.

The leftover Smith-list category **Consolidation / diffusion / transient** is hidden. Transient dissipation lives on the Flow-Seepage **Transient State** leaf.

## Deterministic and probabilistic

| Mode | Behaviour |
|------|-----------|
| **Deterministic** | Single-value material parameters; standard FE / SRF run |
| **Probabilistic** | Enables reliability / sampling workflows (Sensitivity / Probabilistic / Back Analysis tab) |

## Problem dimension

| Option | Use |
|--------|-----|
| **1D Problem** | One-dimensional bar / axial style problems |
| **2D Problem** | Standard 2D slope and continuum models (typical FEM slope work) |
| **3D Problem** | Reserved for 3D problem definition when enabled |

For ordinary slope FE models, choose **2D Problem**.

## Stages

Enable staged analysis and keep the stage count on this page. The stage table stays collapsed until you choose **Edit stages…**.

> **Note:** FE mesh, strength-reduction, and Script Editor workflows are documented under [Finite element analysis](../fem/overview.md). Groundwater behaviour is summarised under [Groundwater overview](../water/groundwater-overview.md).

## See also

- [Project Settings — General](general.md)
- [Groundwater overview](../water/groundwater-overview.md)
- [Quick start: your first FEM analysis](../getting-started/quick-start-fem.md)
- [Field Stations — borehole log to soil block](../tutorials/field-stations-import.md)
