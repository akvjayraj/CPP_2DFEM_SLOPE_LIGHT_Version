# Elastic analysis

Structural – static elasticity checks. Continuum only (no Goodman joint). Each case is a loadable `.xfem`.

| Case | Folder | Meaning |
| ---- | ------ | ------- |
| EL-01 | [EL01_UniaxialColumn](EL01_UniaxialColumn/) | 1×3 m column, top `P = 1` MPa, no joint. Twin of JR-03 rock strain. |

Open the `.xfem` and Run FE (p610 T3, `layers = 0`). Compare `σy = −P` and `u(top) = P H / E`.
