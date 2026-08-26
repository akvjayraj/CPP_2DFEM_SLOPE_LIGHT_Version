#include "elmat.hpp"
#include <cstdio>

namespace fem {

static const double pt25 = 0.25;
static const double pt2 = 0.2;

void elmat(double area, double rho, std::vector<std::vector<double>>& emm) {
  const int ndof = static_cast<int>(emm.size());
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < ndof; ++j)
      emm[i][j] = 0.0;
  if (ndof == 8) {
    for (int i = 0; i < 8; ++i)
      emm[i][i] = pt25 * area * rho;
    return;
  }
  if (ndof == 16) {
    for (int i = 0; i < 16; ++i)
      emm[i][i] = pt2 * area * rho;
    for (int i = 0; i <= 12; i += 4)
      emm[i][i] = pt25 * emm[2][2];
    for (int i = 1; i <= 13; i += 4)
      emm[i][i] = pt25 * emm[2][2];
    return;
  }
  std::fprintf(stderr, "Wrong number of nodes for rectangular element\n");
}

} // namespace fem
