#include "spabac.hpp"
#include <cstdlib>
#include <iostream>

namespace fem {

namespace {

[[nodiscard]] static double kv_get(const std::vector<double>& kv, int idx1, const char* ctx) {
  if (idx1 < 1 || idx1 > static_cast<int>(kv.size())) {
    std::cerr << "spabac: skyline index out of range: 1-based idx=" << idx1 << " size=" << kv.size()
              << " (" << ctx << ")\n";
    std::exit(7);
  }
  return kv[idx1 - 1];
}

} // namespace

void spabac(const std::vector<double>& kv, std::vector<double>& loads,
            const std::vector<int>& kdiag) {
  int n = static_cast<int>(kdiag.size());
  loads[1] = loads[1] / kv_get(kv, 1, "ab1");
  for (int i = 2; i <= n; ++i) {
    int ki = kdiag[i - 1] - i;
    int l = kdiag[i - 2] - ki + 1;
    double x = loads[i];
    if (l != i) {
      for (int j = l; j <= i - 1; ++j) {
        x -= kv_get(kv, ki + j, "ab2") * loads[j];
      }
    }
    loads[i] = x / kv_get(kv, ki + i, "ab3");
  }
  for (int it = 2; it <= n; ++it) {
    int i = n + 2 - it;
    int ki = kdiag[i - 1] - i;
    double x = loads[i] / kv_get(kv, ki + i, "ab4");
    loads[i] = x;
    int l = kdiag[i - 2] - ki + 1;
    if (l != i) {
      for (int k = l; k <= i - 1; ++k) {
        loads[k] -= x * kv_get(kv, ki + k, "ab5");
      }
    }
  }
  loads[1] = loads[1] / kv_get(kv, 1, "ab6");
}

} // namespace fem
