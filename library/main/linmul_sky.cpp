#include "linmul_sky.hpp"

namespace fem {

void linmul_sky(const std::vector<double>& kv, const std::vector<double>& disps,
                std::vector<double>& loads, const std::vector<int>& kdiag) {
  int n = static_cast<int>(disps.size()) - 1;
  for (int i = 1; i <= n; ++i) {
    double x = 0.0;
    int lup = kdiag[i - 1];
    int low = (i == 1) ? lup : kdiag[i - 2] + 1;
    for (int j = low; j <= lup; ++j)
      x += kv[j - 1] * disps[i + j - lup];
    loads[i] = x;
    if (i == 1) continue;
    // Fortran decrements lup before this loop and uses it in k=i+j-lup-1,
    // so with the original lup the column index is k = i+j-lup.
    int lup2 = lup - 1;
    for (int j = low; j <= lup2; ++j) {
      int k = i + j - lup;
      loads[k] += kv[j - 1] * disps[i];
    }
  }
}

} // namespace fem
