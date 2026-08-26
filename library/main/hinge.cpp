#include "hinge.hpp"
#include <cmath>

namespace fem {

namespace {
void loc_to_glob(const std::vector<double>& local, std::vector<double>& global,
                 double gamma, const std::vector<std::vector<double>>& coord) {
  const double zero = 0.0, one = 1.0, d180 = 180.0;
  double x1 = coord[0][0], y1 = coord[0][1], z1 = coord[0][2];
  double x2 = coord[1][0], y2 = coord[1][1], z2 = coord[1][2];
  double xl = x2 - x1, yl = y2 - y1, zl = z2 - z1;
  double ell = std::sqrt(xl * xl + yl * yl + zl * zl);
  double pi = std::acos(-one);
  double gamrad = gamma * pi / d180;
  double cg = std::cos(gamrad), sg = std::sin(gamrad);
  double den = ell * std::sqrt(xl * xl + zl * zl);
  double r0[3][3];
  if (std::abs(den) > 1e-30) {
    r0[0][0] = xl / ell; r0[1][0] = yl / ell; r0[2][0] = zl / ell;
    r0[0][1] = (-xl * yl * cg - ell * zl * sg) / den;
    r0[1][1] = den * cg / (ell * ell);
    r0[2][1] = (-yl * zl * cg + ell * xl * sg) / den;
    r0[0][2] = (xl * yl * sg - ell * zl * cg) / den;
    r0[1][2] = -den * sg / (ell * ell);
    r0[2][2] = (yl * zl * sg + ell * xl * cg) / den;
  } else {
    r0[0][0] = r0[2][0] = r0[1][1] = r0[1][2] = zero;
    r0[1][0] = one; r0[0][1] = -cg; r0[2][2] = cg; r0[2][1] = sg; r0[0][2] = sg;
  }
  for (int i = 0; i < 12; ++i) {
    global[i] = zero;
    for (int j = 0; j < 12; ++j) {
      int bi = i / 3, bj = j / 3;
      if (bi == bj) global[i] += r0[i % 3][j % 3] * local[j];
    }
  }
}

void glob_to_loc(const std::vector<double>& global, std::vector<double>& local,
                 double gamma, const std::vector<std::vector<double>>& coord) {
  const double zero = 0.0, one = 1.0, d180 = 180.0;
  double x1 = coord[0][0], y1 = coord[0][1], z1 = coord[0][2];
  double x2 = coord[1][0], y2 = coord[1][1], z2 = coord[1][2];
  double xl = x2 - x1, yl = y2 - y1, zl = z2 - z1;
  double ell = std::sqrt(xl * xl + yl * yl + zl * zl);
  double pi = std::acos(-one);
  double gamrad = gamma * pi / d180;
  double cg = std::cos(gamrad), sg = std::sin(gamrad);
  double den = ell * std::sqrt(xl * xl + zl * zl);
  double t[12][12];
  for (int i = 0; i < 12; ++i) for (int j = 0; j < 12; ++j) t[i][j] = zero;
  if (std::abs(den) > 1e-30) {
    double r0[3][3];
    r0[0][0] = xl / ell; r0[0][1] = yl / ell; r0[0][2] = zl / ell;
    r0[1][0] = (-xl * yl * cg - ell * zl * sg) / den;
    r0[1][1] = den * cg / (ell * ell);
    r0[1][2] = (-yl * zl * cg + ell * xl * sg) / den;
    r0[2][0] = (xl * yl * sg - ell * zl * cg) / den;
    r0[2][1] = -den * sg / (ell * ell);
    r0[2][2] = (yl * zl * sg + ell * xl * cg) / den;
    for (int i = 0; i < 3; ++i)
      for (int j = 0; j < 3; ++j)
        for (int k = 0; k <= 9; k += 3)
          t[i + k][j + k] = r0[i][j];
  } else {
    double r0[3][3] = {{zero, one, zero}, {-cg, zero, sg}, {sg, zero, cg}};
    for (int i = 0; i < 3; ++i)
      for (int j = 0; j < 3; ++j)
        for (int k = 0; k <= 9; k += 3)
          t[i + k][j + k] = r0[i][j];
  }
  for (int i = 0; i < 12; ++i) {
    local[i] = zero;
    for (int j = 0; j < 12; ++j) local[i] += t[i][j] * global[j];
  }
}
}  // namespace

void hinge(const std::vector<std::vector<double>>& coord,
           const std::vector<std::vector<double>>& holdr,
           const std::vector<double>& action, std::vector<double>& react,
           const std::vector<std::vector<double>>& prop,
           const std::vector<int>& etype, int iel,
           const std::vector<double>& gamma) {
  const int ndim = static_cast<int>(coord[0].size());
  const int ndof = static_cast<int>(action.size());
  const double zero = 0.0;

  std::vector<double> total(ndof);
  for (int i = 0; i < ndof; ++i) total[i] = holdr[i][iel];

  double bm1 = zero, bm2 = zero, bm3 = zero, bm4 = zero;

  if (ndim == 1) {
    double mpy = prop[1][etype[iel] - 1];
    double ell = coord[1][0] - coord[0][0];
    double s1 = total[1] + action[1], s2 = total[3] + action[3];
    if (std::abs(s1) > mpy) bm1 = (s1 > zero) ? (mpy - s1) : (-mpy - s1);
    if (std::abs(s2) > mpy) bm2 = (s2 > zero) ? (mpy - s2) : (-mpy - s2);
    react[0] = (bm1 + bm2) / ell;
    react[1] = bm1;
    react[2] = -react[0];
    react[3] = bm2;
  } else if (ndim == 2) {
    double mpy = prop[2][etype[iel] - 1];
    double x1 = coord[0][0], y1 = coord[0][1];
    double x2 = coord[1][0], y2 = coord[1][1];
    double ell = std::sqrt((y2 - y1) * (y2 - y1) + (x2 - x1) * (x2 - x1));
    double csch = (x2 - x1) / ell, snch = (y2 - y1) / ell;
    double s1 = total[2] + action[2], s2 = total[5] + action[5];
    if (std::abs(s1) > mpy) bm1 = (s1 > zero) ? (mpy - s1) : (-mpy - s1);
    if (std::abs(s2) > mpy) bm2 = (s2 > zero) ? (mpy - s2) : (-mpy - s2);
    react[0] = -(bm1 + bm2) * snch / ell;
    react[1] = (bm1 + bm2) * csch / ell;
    react[2] = bm1;
    react[3] = -react[0];
    react[4] = -react[1];
    react[5] = bm2;
  } else if (ndim == 3) {
    double gam = gamma[iel];
    double mpy = prop[4][etype[iel] - 1];
    double mpz = prop[5][etype[iel] - 1];
    double mpx = prop[6][etype[iel] - 1];
    double x1 = coord[0][0], y1 = coord[0][1], z1 = coord[0][2];
    double x2 = coord[1][0], y2 = coord[1][1], z2 = coord[1][2];
    double ell = std::sqrt((z2 - z1) * (z2 - z1) + (y2 - y1) * (y2 - y1) + (x2 - x1) * (x2 - x1));
    std::vector<double> global(12), local(12);
    for (int i = 0; i < 12; ++i) global[i] = total[i] + action[i];
    glob_to_loc(global, local, gam, coord);
    double s1 = local[4], s2 = local[10];
    if (std::abs(s1) > mpy) bm1 = (s1 > zero) ? (mpy - s1) : (-mpy - s1);
    if (std::abs(s2) > mpy) bm2 = (s2 > zero) ? (mpy - s2) : (-mpy - s2);
    local[2] = -(bm1 + bm2) / ell;
    local[8] = -local[2];
    local[4] = bm1;
    local[10] = bm2;
    double s3 = local[5], s4 = local[11];
    if (std::abs(s3) > mpz) bm3 = (s3 > zero) ? (mpz - s3) : (-mpz - s3);
    if (std::abs(s4) > mpz) bm4 = (s4 > zero) ? (mpz - s4) : (-mpz - s4);
    local[1] = (bm3 + bm4) / ell;
    local[7] = -local[1];
    local[5] = bm3;
    local[11] = bm4;
    double s5 = local[3];
    if (std::abs(s5) > mpx) global[3] = (s5 > zero) ? (mpx - s5) : (-mpx - s5);
    else global[3] = zero;
    local[9] = -local[3];
    loc_to_glob(local, react, gam, coord);
    react[3] = global[3];
  }
}

} // namespace fem
