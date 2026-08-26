# Verification Master Index

| Field | Value |
|---|---|
| Root | Verification |
| Generated at | 2026-05-06 13:07:49 +05:30 |
| Commit | 4a6b63d |

## Suite Dashboard

| Suite | Cases | Non-pass cases | Verdict |
|---|---:|---:|---|
| multi_element/geotech | 27 | 0 | PASS |
| multi_element/kirsch_canonical | 1 | 0 | PASS |
| multi_element/mc_tunnel_canonical | 28 | 0 | PASS |
| multi_element/nafems_index | 10 | 0 | PASS |
| multi_element/salencon | 1 | 0 | PASS |
| single_element/boundary_distance | 12 | 0 | PASS |
| single_element/cc_mc_tunnel | 4 | 0 | PASS |
| single_element/dp_vs_mc | 8 | 0 | PASS |
| single_element/hb_rock_tunnel | 7 | 0 | PASS |
| single_element/kirsch_elastic | 20 | 0 | PASS |
| single_element/lame_cylinder | 4 | 0 | PASS |
| single_element/mesh_convergence | 12 | 0 | PASS |
| single_element/peck_mair_settlement | 4 | 0 | PASS |
| single_element/stress_rotation | 16 | 0 | PASS |
| single_element/tunnel_near_slope | 4 | 0 | PASS |
| single_element/twin_tunnel | 8 | 0 | PASS |

## Discovered Suite Summaries

- Verification\multi_element\geotech\Geotech_All_SmokeTests_Summary.csv
- Verification\multi_element\geotech\Slope_All_SmokeTests_Summary.csv
- Verification\multi_element\kirsch_canonical\Kirsch_All_SmokeTests_Summary.csv
- Verification\multi_element\mc_tunnel_canonical\MC_Tunnel_All_SmokeTests_Summary.csv
- Verification\multi_element\nafems_index\NAFEMS_All_SmokeTests_Summary.csv
- Verification\multi_element\salencon\Salencon_All_SmokeTests_Summary.csv
- Verification\single_element\boundary_distance\Boundary_Distance_Sensitivity_All_SmokeTests_Summary.csv
- Verification\single_element\cc_mc_tunnel\Convergence_Confinement_MC_Tunnel_All_SmokeTests_Summary.csv
- Verification\single_element\dp_vs_mc\Drucker_Prager_vs_Mohr_Coulomb_All_SmokeTests_Summary.csv
- Verification\single_element\hb_rock_tunnel\Hoek_Brown_Rock_Tunnel_All_SmokeTests_Summary.csv
- Verification\single_element\kirsch_elastic\Kirsch_Elastic_Tunnel_All_SmokeTests_Summary.csv
- Verification\single_element\lame_cylinder\Lame_Thick_Cylinder_All_SmokeTests_Summary.csv
- Verification\single_element\mesh_convergence\Mesh_Convergence_Suite_All_SmokeTests_Summary.csv
- Verification\single_element\peck_mair_settlement\Peck_Mair_Shallow_Tunnel_Settlement_All_SmokeTests_Summary.csv
- Verification\single_element\stress_rotation\Stress_Rotation_Case_All_SmokeTests_Summary.csv
- Verification\single_element\tunnel_near_slope\Tunnel_Near_Slope_Or_Surface_All_SmokeTests_Summary.csv
- Verification\single_element\twin_tunnel\Twin_Tunnel_Interaction_All_SmokeTests_Summary.csv

## Artifacts

- _index.csv: machine-readable rollup of all discovered suite summaries.
- _index.md: human-readable dashboard for quick health checks.

## Conventions

- A new suite is auto-discovered when its top-level folder under Verification/
  contains a file named *_All_SmokeTests_Summary.csv with the standard
  Folder/Case/MeshType/FailureCriterion/MaterialType/Verdict/Notes columns.
- Run this script after rebuilding any suite to refresh the master dashboard.