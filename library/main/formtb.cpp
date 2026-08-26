#include "formtb.hpp"

namespace fem {

void formtb(std::vector<std::vector<double>>& pb, const std::vector<std::vector<double>>& km, const std::vector<int>& g) {
  const int idof = static_cast<int>(km.size());
  const int iw = (static_cast<int>(pb[0].size()) - 1) / 2;
  for (int i = 0; i < idof; ++i) {
    if (g[i] == 0) continue;
    for (int j = 0; j < idof; ++j) {
      if (g[j] == 0) continue;
      int icd = g[j] - g[i] + iw + 1;
      int row = g[i] - 1;
      int col = icd - 1;
      if (row >= 0 && row < static_cast<int>(pb.size()) && col >= 0 && col < static_cast<int>(pb[0].size()))
        pb[row][col] += km[i][j];
    }
  }
}

} // namespace fem
