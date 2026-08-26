#ifndef GEOM_GEOM_FREESURF_HPP
#define GEOM_GEOM_FREESURF_HPP

#include <vector>

namespace geom {

/// Forms coordinates and steering for 4-node quads (Laplace, variable mesh,
/// 1 DOF per node). Numbering in x-direction.
void geom_freesurf(int iel, int nxe, int fixed_seep, int fixed_down, double down,
                   const std::vector<double>& width, const std::vector<double>& angs,
                   const std::vector<double>& surf,
                   std::vector<std::vector<double>>& coord, std::vector<int>& num);

} // namespace geom

#endif
