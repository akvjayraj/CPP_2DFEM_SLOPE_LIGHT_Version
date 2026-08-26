#include "checon.hpp"
#include <algorithm>
#include <cmath>

namespace fem {

bool checon(const std::vector<double>& loads, std::vector<double>& oldlds,
            double tol) {
  double max_loads = 0.0;
  for (size_t i = 1; i < loads.size(); ++i)
    if (std::abs(loads[i]) > max_loads) max_loads = std::abs(loads[i]);
  if (max_loads < 1e-30) return true;
  double max_diff = 0.0;
  for (size_t i = 1; i < loads.size(); ++i)
    if (std::abs(loads[i] - oldlds[i]) > max_diff)
      max_diff = std::abs(loads[i] - oldlds[i]);
  oldlds = loads;
  return (max_diff / max_loads <= tol);
}

} // namespace fem
