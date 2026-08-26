#include "formnf.hpp"

namespace geom {

void formnf(std::vector<int>& nf, int nodof, int nn) {
  int m = 0;
  for (int j = 0; j < nn; ++j) {
    for (int i = 0; i < nodof; ++i) {
      int idx = i + j * nodof;
      if (nf[idx] != 0) {
        ++m;
        nf[idx] = m;
      }
    }
  }
}

} // namespace geom
