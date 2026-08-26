#include "glob_to_loc.hpp"
#include <cmath>

namespace fem {

static const double d180 = 180.0;

void glob_to_loc(const std::vector<double>& global, std::vector<double>& local, double gamma,
                 const std::vector<std::vector<double>>& coord) {
  int ndim = static_cast<int>(coord[0].size());
  if (ndim == 2) {
    double x1 = coord[0][0], y1 = coord[0][1];
    double x2 = coord[1][0], y2 = coord[1][1];
    double ell = std::sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    double cg = (x2 - x1) / ell, sg = (y2 - y1) / ell;
    local[0] = cg * global[0] + sg * global[1];
    local[1] = cg * global[1] - sg * global[0];
    local[2] = global[2];
    local[3] = cg * global[3] + sg * global[4];
    local[4] = cg * global[4] - sg * global[3];
    local[5] = global[5];
    return;
  }
  double x1 = coord[0][0], y1 = coord[0][1], z1 = coord[0][2];
  double x2 = coord[1][0], y2 = coord[1][1], z2 = coord[1][2];
  double xl = x2 - x1, yl = y2 - y1, zl = z2 - z1;
  double ell = std::sqrt(xl * xl + yl * yl + zl * zl);
  double r0[3][3];
  double pi = std::acos(-1.0);
  double gamrad = gamma * pi / d180;
  double cg = std::cos(gamrad), sg = std::sin(gamrad);
  double den = ell * std::sqrt(xl * xl + zl * zl);
  if (std::abs(den) > 1e-30) {
    r0[0][0] = xl / ell; r0[0][1] = yl / ell; r0[0][2] = zl / ell;
    r0[1][0] = (-xl * yl * cg - ell * zl * sg) / den;
    r0[1][1] = den * cg / (ell * ell);
    r0[1][2] = (-yl * zl * cg + ell * xl * sg) / den;
    r0[2][0] = (xl * yl * sg - ell * zl * cg) / den;
    r0[2][1] = -den * sg / (ell * ell);
    r0[2][2] = (yl * zl * sg + ell * xl * cg) / den;
  } else {
    r0[0][0] = r0[0][2] = r0[1][1] = r0[2][1] = 0.0;
    r0[0][1] = 1.0; r0[1][0] = -cg; r0[2][2] = cg; r0[1][2] = sg; r0[2][0] = sg;
  }
  for (int i = 0; i < 12; ++i) {
    double sum = 0.0;
    int iblock = i / 3, idim = i % 3;
    for (int j = 0; j < 12; ++j) {
      int jblock = j / 3, jdim = j % 3;
      if (iblock == jblock) sum += r0[idim][jdim] * global[j];
    }
    local[i] = sum;
  }
}

} // namespace fem
