#include "comred.hpp"

namespace fem {

void comred(std::vector<std::complex<double>>& bk, int n) {
  const int iw = static_cast<int>(bk.size()) / n - 1;
  for (int i = 2; i <= n; ++i) {
    int il1 = i - 1;
    int kbl = il1 + iw + 1;
    if (kbl > n) kbl = n;
    for (int j = i; j <= kbl; ++j) {
      int ij = (j - i) * n + i;  // 1-based
      std::complex<double> sum = bk[ij - 1];
      int nkb = j - iw;
      if (nkb <= 0) nkb = 1;
      if (nkb <= il1) {
        for (int m = nkb; m <= il1; ++m) {
          int ni = (i - m) * n + m;  // 1-based
          int nj = (j - m) * n + m;
          sum -= bk[ni - 1] * bk[nj - 1] / bk[m - 1];
        }
      }
      bk[ij - 1] = sum;
    }
  }
}

} // namespace fem
