#include "comsub.hpp"

namespace fem {

void comsub(const std::vector<std::complex<double>>& bk, std::vector<std::complex<double>>& loads) {
  const int n = static_cast<int>(loads.size());
  const int iw = static_cast<int>(bk.size()) / n - 1;
  loads[0] /= bk[0];
  for (int i = 2; i <= n; ++i) {
    std::complex<double> sum = loads[i - 1];
    int i1 = i - 1;
    int nkb = i - iw;
    if (nkb <= 0) nkb = 1;
    for (int k = nkb; k <= i1; ++k) {
      int jn = (i - k) * n + k;  // 1-based
      sum -= bk[jn - 1] * loads[k - 1];
    }
    loads[i - 1] = sum / bk[i - 1];
  }
  for (int jj = 2; jj <= n; ++jj) {
    int i = n - jj + 1;
    std::complex<double> sum = 0.0;
    int i1 = i + 1;
    int nkb = i + iw;
    if (nkb > n) nkb = n;
    for (int k = i1; k <= nkb; ++k) {
      int jn = (k - i) * n + i;
      sum += bk[jn - 1] * loads[k - 1];
    }
    loads[i - 1] -= sum / bk[i - 1];
  }
}

} // namespace fem
