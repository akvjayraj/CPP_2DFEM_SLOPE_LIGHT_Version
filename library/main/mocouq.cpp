#include "mocouq.hpp"
#include <cmath>

namespace fem {

static const double zero = 0.0, pt49 = 0.49, pt5 = 0.5, one = 1.0, d3 = 3.0, d4 = 4.0, d180 = 180.0;

void mocouq(double psi, double dsbar, double theta, double& dq1, double& dq2, double& dq3) {
  double psir = psi * d4 * std::atan(one) / d180;
  double snth = std::sin(theta), snps = std::sin(psir);
  double sq3 = std::sqrt(d3);
  dq1 = snps;
  if (std::abs(snth) > pt49) {
    double c1 = (snth >= zero) ? one : -one;
    dq2 = (sq3 * pt5 - c1 * snps * pt5 / sq3) * sq3 * pt5 / dsbar;
    dq3 = zero;
  } else {
    double csth = std::cos(theta), cs3th = std::cos(d3 * theta);
    double tn3th = std::tan(d3 * theta), tnth = snth / csth;
    dq2 = sq3 * csth / dsbar * ((one + tnth * tn3th) + snps * (tn3th - tnth) / sq3) * pt5;
    dq3 = pt5 * d3 * (sq3 * snth + snps * csth) / (cs3th * dsbar * dsbar);
  }
}

} // namespace fem
