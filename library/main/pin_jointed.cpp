#include "pin_jointed.hpp"
#include <cmath>

namespace fem {

void pin_jointed(std::vector<std::vector<double>>& km, double ea,
                 const std::vector<std::vector<double>>& coord) {
  const int ndim = static_cast<int>(coord[0].size());
  const double one = 1.0;

  if (ndim == 1) {
    double ell = coord[1][0] - coord[0][0];
    km[0][0] = one;
    km[0][1] = -one;
    km[1][0] = -one;
    km[1][1] = one;
    for (int i = 0; i < 2; ++i)
      for (int j = 0; j < 2; ++j)
        km[i][j] *= ea / ell;
    return;
  }

  if (ndim == 2) {
    double x1 = coord[0][0], y1 = coord[0][1];
    double x2 = coord[1][0], y2 = coord[1][1];
    double ell = std::sqrt((y2 - y1) * (y2 - y1) + (x2 - x1) * (x2 - x1));
    double cs = (x2 - x1) / ell;
    double sn = (y2 - y1) / ell;
    double a = cs * cs, b = sn * sn, c = cs * sn;
    km[0][0] = a;   km[0][1] = c;   km[0][2] = -a;  km[0][3] = -c;
    km[1][0] = c;   km[1][1] = b;   km[1][2] = -c;  km[1][3] = -b;
    km[2][0] = -a;  km[2][1] = -c;  km[2][2] = a;   km[2][3] = c;
    km[3][0] = -c;  km[3][1] = -b;  km[3][2] = c;   km[3][3] = b;
    for (int i = 0; i < 4; ++i)
      for (int j = 0; j < 4; ++j)
        km[i][j] *= ea / ell;
    return;
  }

  if (ndim == 3) {
    double xl = coord[1][0] - coord[0][0];
    double yl = coord[1][1] - coord[0][1];
    double zl = coord[1][2] - coord[0][2];
    double ell = std::sqrt(xl * xl + yl * yl + zl * zl);
    xl /= ell; yl /= ell; zl /= ell;
    double a = xl * xl, b = yl * yl, c = zl * zl;
    double d = xl * yl, e = yl * zl, f = zl * xl;
    km[0][0] = a;   km[0][1] = d;   km[0][2] = f;   km[0][3] = -a;  km[0][4] = -d;  km[0][5] = -f;
    km[1][0] = d;   km[1][1] = b;   km[1][2] = e;   km[1][3] = -d;  km[1][4] = -b;  km[1][5] = -e;
    km[2][0] = f;   km[2][1] = e;   km[2][2] = c;   km[2][3] = -f;  km[2][4] = -e;  km[2][5] = -c;
    km[3][0] = -a;  km[3][1] = -d;  km[3][2] = -f;  km[3][3] = a;   km[3][4] = d;   km[3][5] = f;
    km[4][0] = -d;  km[4][1] = -b;  km[4][2] = -e;  km[4][3] = d;   km[4][4] = b;   km[4][5] = e;
    km[5][0] = -f;  km[5][1] = -e;  km[5][2] = -c;  km[5][3] = f;   km[5][4] = e;   km[5][5] = c;
    for (int i = 0; i < 6; ++i)
      for (int j = 0; j < 6; ++j)
        km[i][j] *= ea / ell;
  }
}

} // namespace fem
