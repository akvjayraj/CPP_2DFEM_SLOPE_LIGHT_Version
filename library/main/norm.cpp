#include "norm.hpp"
#include <cmath>

namespace fem {

double norm(const std::vector<double>& x) {
  double sum = 0.0;
  for (size_t i = 0; i < x.size(); ++i)
    sum += x[i] * x[i];
  return std::sqrt(sum);
}

} // namespace fem
