#include "chobk2.hpp"

namespace fem {

void chobk2(const std::vector<std::vector<double>>& kb,
            std::vector<double>& loads) {
  int n = static_cast<int>(kb.size());
  int iw = static_cast<int>(kb[0].size()) - 1;

  loads[n] = loads[n] / kb[n - 1][iw];

  for (int i = n - 2; i >= 0; --i) {
    double x = 0.0;
    int l = i + iw;
    if (i > n - iw - 1) l = n;
    int m = i + 1;
    for (int j = m; j <= l; ++j) {
      x += kb[j][iw + i - j] * loads[j];
    }
    loads[i] = (loads[i] - x) / kb[i][iw];
  }
}

} // namespace fem
