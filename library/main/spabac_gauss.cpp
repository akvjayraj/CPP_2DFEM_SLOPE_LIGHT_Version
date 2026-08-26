#include "spabac_gauss.hpp"

namespace fem {

void spabac_gauss(const std::vector<double>& kv, std::vector<double>& loads,
                  const std::vector<int>& kdiag) {
  int n = static_cast<int>(kdiag.size());
  loads[1] = loads[1] / kv[0];
  for (int i = 2; i <= n; ++i) {
    int ki = kdiag[i - 1] - i;
    int l = kdiag[i - 2] - ki + 1;
    double x = loads[i];
    for (int j = l; j <= i - 1; ++j)
      x -= kv[ki + j - 1] * loads[j];
    loads[i] = x / kv[ki + i - 1];
  }
  for (int it = 2; it <= n; ++it) {
    int i = n + 2 - it;
    int ki = kdiag[i - 1] - i;
    int l = kdiag[i - 2] - ki + 1;
    if (l <= i - 1) {
      for (int k = l; k <= i - 1; ++k)
        loads[k] -= kv[ki + k - 1] * loads[i];
    }
  }
}

} // namespace fem
