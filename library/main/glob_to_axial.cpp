#include "glob_to_axial.hpp"
#include <cmath>

namespace fem {

double glob_to_axial(const std::vector<double>& global,
                     const std::vector<std::vector<double>>& coord) {
  const int ndim = static_cast<int>(coord[0].size());
  double add = 0.0;
  for (int i = 0; i < ndim; ++i)
    add += (coord[1][i] - coord[0][i]) * (coord[1][i] - coord[0][i]);
  double ell = std::sqrt(add);
  double axial = 0.0;
  for (int i = 0; i < ndim; ++i)
    axial += (coord[1][i] - coord[0][i]) / ell * global[ndim + i];
  return axial;
}

} // namespace fem
