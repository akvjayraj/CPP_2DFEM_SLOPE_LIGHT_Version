#include "beam_mm.hpp"

namespace fem {

void beam_mm(double mm[4][4], double fs, double ell) {
  double fac = (fs * ell) / 420.0;

  mm[0][0] = 156.0 * fac;
  mm[2][2] = mm[0][0];
  mm[0][1] = 22.0 * ell * fac;
  mm[1][0] = mm[0][1];
  mm[2][3] = -mm[0][1];
  mm[3][2] = mm[2][3];
  mm[0][2] = 54.0 * fac;
  mm[2][0] = mm[0][2];
  mm[0][3] = -13.0 * ell * fac;
  mm[3][0] = mm[0][3];
  mm[1][2] = -mm[0][3];
  mm[2][1] = mm[1][2];
  mm[1][1] = 4.0 * (ell * ell) * fac;
  mm[3][3] = mm[1][1];
  mm[1][3] = -3.0 * (ell * ell) * fac;
  mm[3][1] = mm[1][3];
}

} // namespace fem
