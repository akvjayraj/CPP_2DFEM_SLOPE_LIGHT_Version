#include "beam_km.hpp"

namespace fem {

void beam_km(double km[4][4], double ei, double ell) {
  const double two = 2.0, d4 = 4.0, d6 = 6.0, d12 = 12.0;
  double ell2 = ell * ell;
  double ell3 = ell2 * ell;

  km[0][0] = d12 * ei / ell3;
  km[2][2] = km[0][0];
  km[0][1] = d6 * ei / ell2;
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
}

} // namespace fem
