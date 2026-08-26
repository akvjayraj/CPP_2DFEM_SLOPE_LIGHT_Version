#include "formkb.hpp"

namespace fem {

void formkb(std::vector<std::vector<double>>& kb, const std::vector<std::vector<double>>& km, const std::vector<int>& g) {
  const int idof = static_cast<int>(km.size());
  const int iw = static_cast<int>(kb[0].size()) - 1;
  for (int i = 0; i < idof; ++i) {
    int gi = g[i];
    if (gi <= 0) continue;
    for (int j = 0; j < idof; ++j) {
      int gj = g[j];
      if (gj <= 0) continue;
      int icd = gj - gi + iw + 1;  // 1-based column in band
      if (icd - iw - 1 <= 0) {     // lower triangle: g(j) <= g(i)
        int row = gi - 1;
        int col = icd - 1;
        if (row < static_cast<int>(kb.size()) && col >= 0 && col < static_cast<int>(kb[0].size()))
          kb[row][col] += km[i][j];
      }
    }
  }
}

} // namespace fem
