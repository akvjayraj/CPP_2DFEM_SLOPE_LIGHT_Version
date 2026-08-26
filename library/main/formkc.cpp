#include "formkc.hpp"

namespace fem {

void formkc(std::vector<std::complex<double>>& bk, const std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& cm, const std::vector<int>& g, int n) {
  const int idof = static_cast<int>(km.size());
  for (int i = 0; i < idof; ++i) {
    if (g[i] == 0) continue;
    for (int j = 0; j < idof; ++j) {
      if (g[j] == 0) continue;
      int icd = g[j] - g[i] + 1;
      if (icd >= 1) {
        int ival = n * (icd - 1) + g[i];
        if (ival > 0 && ival <= static_cast<int>(bk.size()))
          bk[ival - 1] += std::complex<double>(km[i][j], cm[i][j]);
      }
    }
  }
}

} // namespace fem
