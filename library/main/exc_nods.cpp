#include "exc_nods.hpp"

namespace fem {

void exc_nods(int noexe, const std::vector<int>& exele, const std::vector<std::vector<int>>& g_num,
              std::vector<int>& totex, int& ntote, std::vector<std::vector<int>>& nf) {
  int nels = static_cast<int>(g_num[0].size());
  int nod = static_cast<int>(g_num.size());
  ntote += noexe;
  for (int i = 0; i < noexe && i < static_cast<int>(exele.size()); ++i)
    totex[ntote - noexe + i] = exele[i];
  for (int i = 0; i < noexe; ++i) {
    for (int k = 0; k < 8 && k < nod; ++k) {
      int ncheck = g_num[k][exele[i]];
      bool nodex = false;
      for (int iel = 0; iel < nels && !nodex; ++iel) {
        bool modex = false;
        for (int jj = 0; jj < ntote; ++jj)
          if (iel == totex[jj]) { modex = true; break; }
        if (modex) continue;
        for (int jj = 0; jj < nod; ++jj)
          if (ncheck == g_num[jj][iel]) { nodex = true; break; }
      }
      if (!nodex && ncheck > 0 && ncheck <= static_cast<int>(nf[0].size()))
        for (size_t d = 0; d < nf.size(); ++d) nf[d][ncheck-1] = 0;
    }
  }
}

} // namespace fem
