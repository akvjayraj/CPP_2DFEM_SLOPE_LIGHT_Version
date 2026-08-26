#include "rod_mm.hpp"

namespace fem {

void rod_mm(double mm[2][2], double ell) {
  double fac = ell / 6.0;
  mm[0][0] = 2.0 * fac;
  mm[0][1] = fac;
  mm[1][0] = fac;
  mm[1][1] = 2.0 * fac;
}

} // namespace fem
