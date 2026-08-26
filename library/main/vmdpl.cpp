#include "vmdpl.hpp"
#include <cmath>
#include <vector>

namespace fem {

static const double one = 1.0, two = 2.0, d4 = 4.0, d6 = 6.0;

void vmdpl(const std::vector<std::vector<double>>& dee, const std::vector<double>& stress,
           std::vector<std::vector<double>>& pl) {
  const int ih = static_cast<int>(stress.size());
  std::vector<double> dfds(ih), ddfds(ih);
  double sq2 = std::sqrt(two);
  if (ih == 4) {
    double sx = stress[0], sy = stress[1], txy = stress[2], sz = stress[3];
    double t17 = std::sqrt(two * sx * sx - two * sx * sy + two * sy * sy - two * sy * sz +
                          two * sz * sz - two * sz * sx + d6 * txy * txy);
    double t19 = one / sq2 / t17;
    dfds[0] = t19 * (d4 * sx - two * sy - two * sz) / two;
    dfds[1] = t19 * (-two * sx + d4 * sy - two * sz) / two;
    dfds[2] = t19 * d6 * txy;
    dfds[3] = t19 * (-two * sy + d4 * sz - two * sx) / two;
  } else if (ih == 6) {
    double sx = stress[0], sy = stress[1], sz = stress[2];
    double txy = stress[3], tyz = stress[4], tzx = stress[5];
    double t21 = std::sqrt(two * sx * sx - two * sx * sy + two * sy * sy - two * sy * sz +
                          two * sz * sz - two * sz * sx + d6 * (txy * txy + tyz * tyz + tzx * tzx));
    double t23 = one / sq2 / t21;
    dfds[0] = t23 * (d4 * sx - two * sy - two * sz) / two;
    dfds[1] = t23 * (-two * sx + d4 * sy - two * sz) / two;
    dfds[2] = t23 * (-two * sy + d4 * sz - two * sx) / two;
    dfds[3] = t23 * d6 * txy;
    dfds[4] = t23 * d6 * tyz;
    dfds[5] = t23 * d6 * tzx;
  }
  for (int i = 0; i < ih; ++i) {
    ddfds[i] = 0.0;
    for (int j = 0; j < ih; ++j)
      ddfds[i] += dee[i][j] * dfds[j];
  }
  double denom = 0.0;
  for (int i = 0; i < ih; ++i) denom += ddfds[i] * dfds[i];
  for (int i = 0; i < ih; ++i)
    for (int j = 0; j < ih; ++j)
      pl[i][j] = ddfds[i] * ddfds[j] / denom;
}

} // namespace fem
