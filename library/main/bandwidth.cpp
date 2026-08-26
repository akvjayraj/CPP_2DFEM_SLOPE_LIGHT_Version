#include "bandwidth.hpp"
#include <cmath>
#include <algorithm>

namespace fem {

int bandwidth(const std::vector<int>& g) {
  int b = 0;
  for (size_t i = 0; i < g.size(); ++i) {
    if (g[i] <= 0) continue;
    for (size_t j = 0; j < g.size(); ++j) {
      if (g[j] <= 0) continue;
      int d = std::abs(g[i] - g[j]);
      if (d > b) b = d;
    }
  }
  return b;
}

} // namespace fem
