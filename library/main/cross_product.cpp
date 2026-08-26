#include "cross_product.hpp"

namespace fem {

void cross_product(const std::vector<double>& a, const std::vector<double>& b,
                   std::vector<std::vector<double>>& ntn) {
  int n = static_cast<int>(a.size());
  int m = static_cast<int>(b.size());
  if (static_cast<int>(ntn.size()) != static_cast<size_t>(n))
    ntn.resize(n, std::vector<double>(m, 0.0));
  for (int i = 0; i < n; ++i)
    for (int j = 0; j < m; ++j)
      ntn[i][j] = a[i] * b[j];
}

} // namespace fem
