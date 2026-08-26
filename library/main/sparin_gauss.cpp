#include "sparin_gauss.hpp"

namespace fem {

void sparin_gauss(std::vector<double>& kv, const std::vector<int>& kdiag) {
  int n = static_cast<int>(kdiag.size());
  for (int i = 1; i <= n; ++i) {
    int ki = kdiag[i - 1] - i;
    int l = (i == 1) ? 1 : kdiag[i - 2] - ki + 1;
    double x = 0.0;
    for (int j = l; j <= i; ++j) {
      x = kv[ki + j - 1];
      int kj = kdiag[j - 1] - j;
      if (j > 1) {
        int ll = kdiag[j - 2] - kj + 1;
        ll = (ll > l) ? ll : l;
        for (int k = ll; k <= j - 1; ++k)
          x -= kv[ki + k - 1] * kv[kj + k - 1];
      }
      if (j < i)
        kv[ki + j - 1] = x / kv[kj + j - 1];
      else
        kv[ki + i - 1] = x;
    }
  }
}

} // namespace fem
