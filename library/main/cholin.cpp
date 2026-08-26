#include "cholin.hpp"
#include <cmath>

namespace fem {

void cholin(std::vector<std::vector<double>>& kb) {
  int n = static_cast<int>(kb.size());
  int iw = static_cast<int>(kb[0].size()) - 1;

  for (int i = 0; i < n; ++i) {
    double x = 0.0;
    for (int j = 0; j < iw; ++j) {
      x += kb[i][j] * kb[i][j];
    }
    kb[i][iw] = std::sqrt(kb[i][iw] - x);

    for (int k = 0; k < iw; ++k) {
      x = 0.0;
      if (i + k < n) {
        if (k != iw - 1) {
          for (int l = iw - k - 1; l >= 0; --l) {
            x += kb[i + k][l] * kb[i][l + k];
          }
        }
        int ia = i + k;
        int ib = iw - k;
        kb[ia][ib] = (kb[ia][ib] - x) / kb[i][iw];
      }
    }
  }
}

} // namespace fem
