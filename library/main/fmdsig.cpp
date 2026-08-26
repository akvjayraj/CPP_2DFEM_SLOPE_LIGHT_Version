#include "fmdsig.hpp"
#include <cstdio>

namespace fem {

void fmdsig(std::vector<std::vector<double>>& dee, double e, double v) {
  int ih = static_cast<int>(dee.size());
  for (int i = 0; i < ih; ++i)
    for (int j = 0; j < ih; ++j) dee[i][j] = 0.0;
  if (ih == 3) {
    double c = e / (1.0 - v * v);
    dee[0][0] = dee[1][1] = c;
    dee[2][2] = 0.5 * e / (1.0 + v);
    dee[0][1] = dee[1][0] = v * c;
  } else {
    std::fprintf(stderr, "wrong size for dee matrix\n");
  }
}

} // namespace fem
