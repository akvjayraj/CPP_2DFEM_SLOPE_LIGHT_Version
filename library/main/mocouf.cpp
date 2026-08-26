#include "mocouf.hpp"
#include <cmath>

namespace fem {

static const double d4 = 4.0, d3 = 3.0, d180 = 180.0;

double mocouf(double phi, double c, double sigm, double dsbar, double theta) {
  double phir = phi * d4 * std::atan(1.0) / d180;
  double snph = std::sin(phir), csph = std::cos(phir);
  double csth = std::cos(theta), snth = std::sin(theta);
  return snph * sigm + dsbar * (csth / std::sqrt(d3) - snth * snph / d3) - c * csph;
}

} // namespace fem
