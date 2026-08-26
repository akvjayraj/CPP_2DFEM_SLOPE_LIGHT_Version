#include "sparin.hpp"
#include <cmath>
#include <algorithm>
#include <cstdlib>
#include <iostream>

namespace fem {

namespace {

[[nodiscard]] static double& kv_at(std::vector<double>& kv, int idx1, const char* ctx) {
  if (idx1 < 1 || idx1 > static_cast<int>(kv.size())) {
    std::cerr << "sparin: skyline index out of range: 1-based idx=" << idx1 << " size=" << kv.size()
              << " (" << ctx << ")\n";
    std::exit(7);
  }
  return kv[idx1 - 1];
}

[[nodiscard]] static double kv_get(const std::vector<double>& kv, int idx1, const char* ctx) {
  if (idx1 < 1 || idx1 > static_cast<int>(kv.size())) {
    std::cerr << "sparin: skyline index out of range: 1-based idx=" << idx1 << " size=" << kv.size()
              << " (" << ctx << ")\n";
    std::exit(7);
  }
  return kv[idx1 - 1];
}

} // namespace

void sparin(std::vector<double>& kv, const std::vector<int>& kdiag) {
  int n = static_cast<int>(kdiag.size());
  if (n < 1 || kv.empty()) {
    std::cerr << "sparin: empty system.\n";
    std::exit(7);
  }
  double d0 = kv_get(kv, 1, "pivot0");
  if (!std::isfinite(d0) || d0 <= 0.0) {
    std::cerr << "sparin: non-positive or invalid first pivot kv[1]=" << d0
              << " (stiffness may be singular — check BCs, materials, mesh).\n";
    std::exit(8);
  }
  kv_at(kv, 1, "sqrt0") = std::sqrt(d0);
  for (int i = 2; i <= n; ++i) {
    int ki = kdiag[i - 1] - i;
    int l = kdiag[i - 2] - ki + 1;
    double x = 0;
    for (int j = l; j <= i; ++j) {
      x = kv_get(kv, ki + j, "x=kv(ki+j)");
      int kj = kdiag[j - 1] - j;
      if (j != 1) {
        int ll = kdiag[j - 2] - kj + 1;
        ll = std::max(l, ll);
        if (ll != j) {
          for (int k = ll; k <= j - 1; ++k) {
            x -= kv_get(kv, ki + k, "inner1") * kv_get(kv, kj + k, "inner2");
          }
        }
      }
      kv_at(kv, ki + j, "div") = x / kv_get(kv, kj + j, "denom");
    }
    kv_at(kv, ki + i, "final sqrt") = std::sqrt(x);
  }
}

} // namespace fem
