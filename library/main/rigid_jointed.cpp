#include "rigid_jointed.hpp"
#include <cmath>

namespace fem {

void rigid_jointed(std::vector<std::vector<double>>& km,
                   const std::vector<std::vector<double>>& prop,
                   const std::vector<double>& gamma,
                   const std::vector<int>& etype, int iel,
                   const std::vector<std::vector<double>>& coord) {
  const int ndim = static_cast<int>(coord[0].size());
  const double zero = 0.0, pt5 = 0.5, one = 1.0, two = 2.0;
  const double d4 = 4.0, d6 = 6.0, d12 = 12.0, d180 = 180.0;

  if (ndim == 1) {
    double ei = prop[0][etype[iel] - 1];
    double ell = coord[1][0] - coord[0][0];
    double ell3 = ell * ell * ell;
    for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) km[i][j] = zero;
    km[0][0] = d12 * ei / ell3;
    km[2][2] = km[0][0];
    km[0][1] = d6 * ei / (ell * ell);
    km[1][0] = km[0][1];
    km[0][3] = km[0][1];
    km[3][0] = km[0][3];
    km[0][2] = -km[0][0];
    km[2][0] = km[0][2];
    km[2][3] = -km[0][1];
    km[3][2] = km[2][3];
    km[1][2] = km[2][3];
    km[2][1] = km[1][2];
    km[1][1] = d4 * ei / ell;
    km[3][3] = km[1][1];
    km[1][3] = two * ei / ell;
    km[3][1] = km[1][3];
    return;
  }

  if (ndim == 2) {
    double ea = prop[0][etype[iel] - 1];
    double ei = prop[1][etype[iel] - 1];
    double x1 = coord[0][0], y1 = coord[0][1];
    double x2 = coord[1][0], y2 = coord[1][1];
    double ell = std::sqrt((y2 - y1) * (y2 - y1) + (x2 - x1) * (x2 - x1));
    double c = (x2 - x1) / ell, s = (y2 - y1) / ell;
    double e1 = ea / ell, e2 = d12 * ei / (ell * ell * ell);
    double e3 = ei / ell, e4 = d6 * ei / (ell * ell);
    for (int i = 0; i < 6; ++i) for (int j = 0; j < 6; ++j) km[i][j] = zero;
    km[0][0] = c * c * e1 + s * s * e2;
    km[3][3] = km[0][0];
    km[0][1] = s * c * (e1 - e2);
    km[1][0] = km[0][1];
    km[3][4] = km[0][1];
    km[4][3] = km[3][4];
    km[0][2] = -s * e4;
    km[2][0] = km[0][2];
    km[0][5] = km[0][2];
    km[5][0] = km[0][5];
    km[2][3] = s * e4;
    km[3][2] = km[2][3];
    km[3][5] = km[2][3];
    km[5][3] = km[3][5];
    km[0][3] = -km[0][0];
    km[3][0] = km[0][3];
    km[0][4] = s * c * (-e1 + e2);
    km[4][0] = km[0][4];
    km[1][3] = km[0][4];
    km[3][1] = km[1][3];
    km[1][1] = s * s * e1 + c * c * e2;
    km[4][4] = km[1][1];
    km[1][4] = -km[1][1];
    km[4][1] = km[1][4];
    km[1][2] = c * e4;
    km[2][1] = km[1][2];
    km[1][5] = km[1][2];
    km[5][1] = km[1][5];
    km[2][2] = d4 * e3;
    km[5][5] = km[2][2];
    km[2][4] = -c * e4;
    km[4][2] = km[2][4];
    km[4][5] = km[2][4];
    km[5][4] = km[4][5];
    km[2][5] = two * e3;
    km[5][2] = km[2][5];
    return;
  }

  if (ndim == 3) {
    double ea = prop[0][etype[iel] - 1];
    double eiy = prop[1][etype[iel] - 1];
    double eiz = prop[2][etype[iel] - 1];
    double gj = prop[3][etype[iel] - 1];
    double x1 = coord[0][0], y1 = coord[0][1], z1 = coord[0][2];
    double x2 = coord[1][0], y2 = coord[1][1], z2 = coord[1][2];
    double xl = x2 - x1, yl = y2 - y1, zl = z2 - z1;
    double ell = std::sqrt(xl * xl + yl * yl + zl * zl);
    for (int i = 0; i < 12; ++i) for (int j = 0; j < 12; ++j) km[i][j] = zero;
    double a1 = ea / ell;
    double a2 = d12 * eiz / (ell * ell * ell);
    double a3 = d12 * eiy / (ell * ell * ell);
    double a4 = d6 * eiz / (ell * ell);
    double a5 = d6 * eiy / (ell * ell);
    double a6 = d4 * eiz / ell;
    double a7 = d4 * eiy / ell;
    double a8 = gj / ell;
    km[0][0] = a1; km[6][6] = a1; km[0][6] = -a1; km[6][0] = -a1;
    km[1][1] = a2; km[7][7] = a2; km[1][7] = -a2; km[7][1] = -a2;
    km[2][2] = a3; km[8][8] = a3; km[2][8] = -a3; km[8][2] = -a3;
    km[3][3] = a8; km[9][9] = a8; km[3][9] = -a8; km[9][3] = -a8;
    km[4][4] = a7; km[10][10] = a7; km[4][10] = pt5 * a7; km[10][4] = pt5 * a7;
    km[5][5] = a6; km[11][11] = a6; km[5][11] = pt5 * a6; km[11][5] = pt5 * a6;
    km[1][5] = a4; km[5][1] = a4; km[1][11] = a4; km[11][1] = a4;
    km[5][7] = -a4; km[7][5] = -a4; km[7][11] = -a4; km[11][7] = -a4;
    km[4][8] = a5; km[8][4] = a5; km[8][10] = a5; km[10][8] = a5;
    km[2][4] = -a5; km[4][2] = -a5; km[2][10] = -a5; km[10][2] = -a5;

    double pi = std::acos(-one);
    double gamrad = gamma[iel] * pi / d180;
    double cg = std::cos(gamrad), sg = std::sin(gamrad);
    double den = ell * std::sqrt(xl * xl + zl * zl);
    double r0[3][3];
    if (std::abs(den) > 1e-30) {
      r0[0][0] = xl / ell; r0[0][1] = yl / ell; r0[0][2] = zl / ell;
      r0[1][0] = (-xl * yl * cg - ell * zl * sg) / den;
      r0[1][1] = den * cg / (ell * ell);
      r0[1][2] = (-yl * zl * cg + ell * xl * sg) / den;
      r0[2][0] = (xl * yl * sg - ell * zl * cg) / den;
      r0[2][1] = -den * sg / (ell * ell);
      r0[2][2] = (yl * zl * sg + ell * xl * cg) / den;
    } else {
      r0[0][0] = zero; r0[0][2] = zero; r0[1][1] = zero; r0[2][1] = zero;
      r0[0][1] = one; r0[1][0] = -cg; r0[2][2] = cg; r0[1][2] = sg; r0[2][0] = sg;
    }
    double t[12][12], tt[12][12], cc[12][12];
    for (int i = 0; i < 12; ++i) for (int j = 0; j < 12; ++j) t[i][j] = tt[i][j] = zero;
    for (int i = 0; i < 3; ++i) {
      for (int j = 0; j < 3; ++j) {
        double x = r0[i][j];
        for (int k = 0; k <= 9; k += 3) {
          t[i + k][j + k] = x;
          tt[j + k][i + k] = x;
        }
      }
    }
    for (int i = 0; i < 12; ++i) {
      for (int j = 0; j < 12; ++j) {
        double sum = zero;
        for (int k = 0; k < 12; ++k) sum += km[i][k] * t[k][j];
        cc[i][j] = sum;
      }
    }
    for (int i = 0; i < 12; ++i) {
      for (int j = 0; j < 12; ++j) {
        double sum = zero;
        for (int k = 0; k < 12; ++k) sum += tt[i][k] * cc[k][j];
        km[i][j] = sum;
      }
    }
  }
}

} // namespace fem
