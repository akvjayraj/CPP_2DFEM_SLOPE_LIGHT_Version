#ifndef GEOM_HEXAHEDRON_XZ_HPP
#define GEOM_HEXAHEDRON_XZ_HPP

#include <vector>

namespace geom {

/// Generates nodal coordinates and numbering for 8, 14 or 20-node hexahedra.
/// Counting x-z planes in the y-direction.
void hexahedron_xz(int iel,
                  const std::vector<double>& x_coords,
                  const std::vector<double>& y_coords,
                  const std::vector<double>& z_coords,
                  std::vector<std::vector<double>>& coord,
                  std::vector<int>& num);

} // namespace geom

#endif
