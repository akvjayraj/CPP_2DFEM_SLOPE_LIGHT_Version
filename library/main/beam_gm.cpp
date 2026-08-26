#include "beam_gm.hpp"

namespace fem {

void beam_gm(double gm[4][4], double ell) {
  const double pt1 = 0.1, opt2 = 1.2, two = 2.0;
  const double d15 = 15.0, d30 = 30.0;
  gm[0][0] = opt2 / ell;
  gm[0][1] = pt1;
  gm[1][0] = pt1;
  gm[0][2] = -opt2 / ell;
  gm[2][0] = -opt2 / ell;
  gm[0][3] = pt1;
  gm[3][0] = pt1;
  gm[1][1] = two * ell / d15;
  gm[1][2] = -pt1;
  gm[2][1] = -pt1;
  gm[1][3] = -ell / d30;
  gm[3][1] = -ell / d30;
  gm[2][2] = opt2 / ell;
  gm[2][3] = -pt1;
  gm[3][2] = -pt1;
  gm[3][3] = two * ell / d15;
}

} // namespace fem
