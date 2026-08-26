#include "chobk1.hpp"

namespace fem {

void chobk1(const std::vector<std::vector<double>>& kb,
            std::vector<double>& loads) {
  int n = static_cast<int>(kb.size());
  int iw = static_cast<int>(kb[0].size()) - 1;

  loads[1] = loads[1] / kb[0][iw];

  for (int i = 1; i < n; ++i) {
    double x = 0.0;
    int k = 0;
    if (i <= iw) k = iw - i + 1;
    for (int j = k; j < iw; ++j) {
      x += kb[i][j] * loads[i + j - iw];
    }
    loads[i] = (loads[i] - x) / kb[i][iw];
  }
}

} // namespace fem
