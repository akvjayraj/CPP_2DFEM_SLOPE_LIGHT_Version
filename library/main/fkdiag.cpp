#include "fkdiag.hpp"
#include <algorithm>

namespace fem {

void fkdiag(std::vector<int>& kdiag, const std::vector<int>& g, int neq) {
  int idof = static_cast<int>(g.size());
  for (int i = 0; i < idof; ++i) {
    int iwp1 = 1;
    if (g[i] != 0) {
      for (int j = 0; j < idof; ++j) {
        if (g[j] != 0) {
          int im = g[i] - g[j] + 1;
          if (im > iwp1) iwp1 = im;
        }
      }
      int k = g[i];
      if (iwp1 > kdiag[k - 1]) kdiag[k - 1] = iwp1;
    }
  }
}

} // namespace fem
