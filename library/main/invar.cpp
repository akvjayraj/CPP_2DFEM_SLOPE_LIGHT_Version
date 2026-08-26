#include "invar.hpp"
#include <cmath>
#include <cstdio>

namespace fem {

static const double small = 1e-10;
static const double one = 1.0;
static const double two = 2.0;
static const double three = 3.0;
static const double six = 6.0;
static const double thpt5 = 13.5;

void invar(const std::vector<double>& stress,
           double* sigm, double* dsbar, double* theta) {
  const int nst = static_cast<int>(stress.size());
  if (nst == 4) {
    double sx = stress[0], sy = stress[1], txy = stress[2], sz = stress[3];
    double sm = (sx + sy + sz) / three;
    double ds = std::sqrt((sx - sy) * (sx - sy) + (sy - sz) * (sy - sz) +
                         (sz - sx) * (sz - sx) + six * txy * txy) / std::sqrt(two);
    if (sigm) *sigm = sm;
    if (dsbar) *dsbar = ds;
    double th = 0.0;
    if (ds >= small) {
      double dx = (two * sx - sy - sz) / three;
      double dy = (two * sy - sz - sx) / three;
      double dz = (two * sz - sx - sy) / three;
      double xj3 = dx * dy * dz - dz * txy * txy;
      double sine = -thpt5 * xj3 / (ds * ds * ds);
      if (sine > one) sine = one;
      if (sine < -one) sine = -one;
      th = std::asin(sine) / three;
    }
    if (theta) *theta = th;
    return;
  }
  if (nst == 6) {
    double sq3 = std::sqrt(three);
    double s1 = stress[0], s2 = stress[1], s3 = stress[2];
    double s4 = stress[3], s5 = stress[4], s6 = stress[5];
    double sm = (s1 + s2 + s3) / three;
    double d2 = ((s1 - s2) * (s1 - s2) + (s2 - s3) * (s2 - s3) + (s3 - s1) * (s3 - s1)) / six + s4 * s4 + s5 * s5 + s6 * s6;
    double ds1 = s1 - sm, ds2 = s2 - sm, ds3 = s3 - sm;
    double d3 = ds1 * ds2 * ds3 - ds1 * s5 * s5 - ds2 * s6 * s6 - ds3 * s4 * s4 + two * s4 * s5 * s6;
    double ds = sq3 * std::sqrt(d2);
    if (sigm) *sigm = sm;
    if (dsbar) *dsbar = ds;
    double th = 0.0;
    if (ds >= small) {
      double sine = -three * sq3 * d3 / (two * std::pow(std::sqrt(d2), 3));
      if (sine > one) sine = one;
      if (sine < -one) sine = -one;
      th = std::asin(sine) / three;
    }
    if (theta) *theta = th;
    return;
  }
  std::fprintf(stderr, "wrong size for nst in invar\n");
}

} // namespace fem
