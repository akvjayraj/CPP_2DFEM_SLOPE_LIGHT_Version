#include "formku.hpp"

namespace fem {

void formku(std::vector<std::vector<double>>& ku, const std::vector<std::vector<double>>& km, const std::vector<int>& g) {
  const int ndof = static_cast<int>(km.size());
  for (int i = 0; i < ndof; ++i) {
    int gi = g[i];
    if (gi == 0) continue;
    for (int j = 0; j < ndof; ++j) {
      int gj = g[j];
      if (gj == 0) continue;
      int icd = gj - gi + 1;  // 1-based column in upper band
      if (icd >= 1) {
        int row = gi - 1;
        int col = icd - 1;
        if (row < static_cast<int>(ku.size()) && col < static_cast<int>(ku[0].size()))
          ku[row][col] += km[i][j];
      }
    }
  }
}

void formku(std::vector<std::vector<double>>& ku, const double km[4][4], const std::vector<int>& g) {
  std::vector<std::vector<double>> km_vec(4, std::vector<double>(4));
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      km_vec[i][j] = km[i][j];
  formku(ku, km_vec, g);
}

} // namespace fem
