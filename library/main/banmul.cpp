#include "banmul.hpp"

namespace fem {

// kb(i, j) 1-based: K(i, i+j-nband-1). Symmetric band, lower column storage.
void banmul(const std::vector<std::vector<double>>& kb, const std::vector<double>& loads, std::vector<double>& ans) {
  const int neq = static_cast<int>(kb.size());
  const int nband = static_cast<int>(kb[0].size()) - 1;
  for (int i = 0; i < neq; ++i) {
    double x = 0.0;
    for (int j = 0; j <= nband; ++j) {
      int col = i - nband + j;
      if (col >= 0) x += kb[i][j] * loads[col];
    }
    for (int row = i + 1; row <= i + nband && row < neq; ++row) {
      int j = i - row + nband;  // kb(row,j) = K(row, i)
      if (j >= 0 && j <= nband) x += kb[row][j] * loads[row];
    }
    ans[i] = x;
  }
}

} // namespace fem
