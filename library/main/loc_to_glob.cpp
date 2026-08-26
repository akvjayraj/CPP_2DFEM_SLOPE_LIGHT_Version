#include "loc_to_glob.hpp"
#include <cmath>

namespace fem {

static const double d180 = 180.0;

void loc_to_glob(const std::vector<double>& local, std::vector<double>& global, double gamma,
                 const std::vector<std::vector<double>>& coord) {
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
    r0[0][0] = xl / ell; r0[1][0] = yl / ell; r0[2][0] = zl / ell;
    r0[0][1] = (-xl * yl * cg - ell * zl * sg) / den;
    r0[1][1] = den * cg / (ell * ell);
    r0[2][1] = (-yl * zl * cg + ell * xl * sg) / den;
    r0[0][2] = (xl * yl * sg - ell * zl * cg) / den;
    r0[1][2] = -den * sg / (ell * ell);
    r0[2][2] = (yl * zl * sg + ell * xl * cg) / den;
  } else {
    r0[0][0] = r0[2][0] = r0[1][1] = r0[1][2] = 0.0;
    r0[1][0] = 1.0; r0[0][1] = -cg; r0[2][2] = cg; r0[2][1] = sg; r0[0][2] = sg;
  }
  for (int i = 0; i < 12; ++i) {
    double sum = 0.0;
    int iblock = i / 3;
    int idim = i % 3;
    for (int j = 0; j < 12; ++j) {
      int jblock = j / 3, jdim = j % 3;
      if (iblock == jblock) sum += r0[idim][jdim] * local[j];
    }
    global[i] = sum;
  }
}

} // namespace fem
