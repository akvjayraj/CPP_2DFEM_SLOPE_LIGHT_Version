#include "gauss_band.hpp"
#include <cmath>
#include <cstdio>

namespace fem {

static const double small = 1e-10;

void gauss_band(std::vector<std::vector<double>>& pb, std::vector<std::vector<double>>& work) {
  const int n = static_cast<int>(pb.size());
  const int iwp1 = (static_cast<int>(pb[0].size()) - 1) / 2 + 1;
  const int iq = 2 * iwp1 - 1;
  int iqp = iwp1;
  const int iwp11 = iwp1 - 1;

  for (int i = 0; i < iwp11; ++i) {
    for (int j = 0; j < iq; ++j) {
      if (j >= iwp1 + i) {
        pb[i][j] = 0.0;
        pb[n - 1 - i][j] = 0.0;
      } else {
        pb[i][j] = pb[i][j + iwp1 - 1 - i];
      }
    }
  }

  for (int k = 0; k < n; ++k) {
    int l = k + iwp1 - 1;
    if (l >= n) l = n - 1;
    int ip = -1;
    double s = small;
    for (int i = k; i <= l; ++i) {
      if (std::abs(pb[i][0]) <= s) continue;
      s = std::abs(pb[i][0]);
      ip = i;
    }
    if (ip < 0) {
      std::fprintf(stderr, "singular\n");
      break;
    }
    if (k == n - 1) break;
    work[iwp1 - 1][k] = static_cast<double>(ip);
    iqp--;
    int j = iwp1 + ip - k;
    if (iqp < j) iqp = j;
    if (j != iwp1) {
      for (int jj = 0; jj < iqp; ++jj) {
        double t = pb[k][jj];
        pb[k][jj] = pb[ip][jj];
        pb[ip][jj] = t;
      }
    }
    for (int i = k + 1; i <= l; ++i) {
      s = pb[i][0] / pb[k][0];
      for (int j = 1; j < iq; ++j) {
        if (j > iqp)
          pb[i][j - 1] = pb[i][j];
        else
          pb[i][j - 1] = pb[i][j] - s * pb[k][j];
      }
      pb[i][iq - 1] = 0.0;
      work[i - k - 1][k] = s;
    }
  }
}

} // namespace fem
