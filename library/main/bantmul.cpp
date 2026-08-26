#include "bantmul.hpp"

namespace fem {

void bantmul(const std::vector<std::vector<double>>& kb, const std::vector<double>& loads, std::vector<double>& ans) {
  const int n = static_cast<int>(kb.size());
  const int l = static_cast<int>(kb[0].size());
  const int iw = (l - 1) / 2;
  for (int i = 0; i < n; ++i) {
    double x = 0.0;
    int k = iw + 2;
    for (int j = 0; j < l; ++j) {
      k--;
      int m = i - k + 1;
      if (m >= 1 && m <= n) x += kb[i][j] * loads[m - 1];
    }
    ans[i] = x;
  }
}

} // namespace fem
