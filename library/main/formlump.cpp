#include "formlump.hpp"

namespace fem {

void formlump(std::vector<double>& diag, const std::vector<std::vector<double>>& emm, const std::vector<int>& g) {
  const int ndof = static_cast<int>(emm.size());
  for (int i = 0; i < ndof; ++i) {
    int gi = g[i];
    if (gi == 0) continue;
    int idx = gi - 1;
    if (idx < static_cast<int>(diag.size()))
      diag[idx] += emm[i][i];
  }
}

void formlump_rowsum(std::vector<double>& diag, const std::vector<std::vector<double>>& mm, const std::vector<int>& g) {
  const int ndof = static_cast<int>(g.size());
  for (int i = 0; i < ndof; ++i) {
    if (g[i] <= 0) continue;
    for (int j = 0; j < ndof; ++j)
      diag[g[i]] += mm[i][j];
  }
}

void formlump_rowsum(std::vector<double>& diag, const double mm[4][4], const std::vector<int>& g) {
  for (int i = 0; i < 4; ++i) {
    if (g[i] <= 0) continue;
    for (int j = 0; j < 4; ++j)
      diag[g[i]] += mm[i][j];
  }
}

} // namespace fem
