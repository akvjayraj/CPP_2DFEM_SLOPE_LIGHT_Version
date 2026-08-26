#include "mcdpl.hpp"
#include <cmath>
#include <cstdio>

namespace fem {

static const double one = 1.0, two = 2.0, d3 = 3.0, d4 = 4.0, d6 = 6.0, d180 = 180.0, pt49 = 0.49;

void mcdpl(double phi, double psi, const std::vector<std::vector<double>>& dee,
           const std::vector<double>& stress, std::vector<std::vector<double>>& pl) {
  const int ih = static_cast<int>(stress.size());
  std::vector<double> dfds(ih), dqds(ih), ddqds(ih), dfdsd(ih);
  double pi = std::acos(-one);
  double phir = phi * pi / d180, psir = psi * pi / d180;
  double snph = std::sin(phir), snps = std::sin(psir);
  double sq3 = std::sqrt(d3);

  if (ih == 4) {
    double sx = stress[0], sy = stress[1], txy = stress[2], sz = stress[3];
    double t4 = (sx + sy + sz) / d3;
    double t8 = sz - t4;
    double t10 = txy * txy;
    double t16 = (sx - sy) * (sx - sy) + (sy - sz) * (sy - sz) + (sz - sx) * (sz - sx);
    double t25 = std::sqrt(t16 / d6 + t10);
    double t26 = t25 * t25;
    double t30 = d3 * sq3 * ((sx - t4) * (sy - t4) * t8 - t8 * t10) / two / t26 / t25;
    if (t30 > one) t30 = one;
    if (t30 < -one) t30 = -one;
    double t33 = std::sin(std::asin(t30) / d3);
    double snth = -t33;

    if (std::abs(snth) > pt49) {
      double pm = (snth < 0) ? one : -one;
      double t2 = snph / d3;
      double t14 = std::sqrt(t16 / d6 + t10);
      double t17 = one / t14 / sq3;
      double t19 = t17 * 0.5;
      double t21 = d3 + pm * snph;
      dfds[0] = t2 + t19 * t21 * (d4 * sx - two * sy - two * sz) / d6;
      dfds[1] = t2 + t19 * t21 * (-two * sx + d4 * sy - two * sz) / d6;
      dfds[2] = t17 * 0.5 * t21 * txy;
      dfds[3] = t2 + t19 * t21 * (-two * sy + d4 * sz - two * sx) / d6;
      t2 = snps / d3;
      t21 = d3 + pm * snps;
      dqds[0] = t2 + t19 * t21 * (d4 * sx - two * sy - two * sz) / d6;
      dqds[1] = t2 + t19 * t21 * (-two * sx + d4 * sy - two * sz) / d6;
      dqds[2] = t17 * 0.5 * t21 * txy;
      dqds[3] = t2 + t19 * t21 * (-two * sy + d4 * sz - two * sx) / d6;
    } else {
      double t2 = snph / d3;
      double t13 = t16 / d6 + t10;
      double t14 = std::sqrt(t13);
      double t18 = (sx + sy + sz) / d3;
      double t19 = sx - t18, t20 = sy - t18, t22 = sz - t18;
      double t25 = t19 * t20 * t22 - t22 * t10;
      double t28 = t14 * t14;
      double t30 = one / t28 / t14;
      double t31 = d3 * sq3 * t25 * 0.5 * t30;
      if (t31 > one) t31 = one;
      if (t31 < -one) t31 = -one;
      double t34 = std::asin(t31) / d3;
      double t35 = std::cos(t34), t36 = std::sin(t34);
      double t41 = one / t14 * (t35 + t36 * snph / sq3);
      double t46 = (d4 * sx - two * sy - two * sz) / d6;
      double t49 = one - one / d3;
      double t53 = t19 / d3 * t22, t54 = t19 * t20 / d3, t55 = t10 / d3;
      double t60 = d3 * sq3 * t25;
      double t61 = t28 * t28;
      double t64 = 0.5 / t61 / t14;
      double t68 = d3 * sq3 * (t49 * t20 * t22 - t53 - t54 + t55) * 0.5 * t30 - d3 / two * t60 * t64 * t46;
      double t83 = std::sqrt(one - d3 * d3 * sq3 * sq3 * t25 * t25 / 4.0 / (t13 * t13) / t13);
      double t84 = one / t83 / d3;
      double t89 = t2 / sq3;
      double t97 = (-two * sx + d4 * sy - two * sz) / d6;
      double t101 = t20 * t22 / d3;
      double t111 = d3 * sq3 * (-t101 + t19 * t49 * t22 - t54 + t55) * 0.5 * t30 - d3 / two * t60 * t64 * t97;
      double t129 = -two * d3 * sq3 * t22 * txy * 0.5 * t30 - d3 * t60 * t64 * txy;
      double t140 = (-two * sy + d4 * sz - two * sx) / d6;
      double t152 = d3 * sq3 * (-t101 - t53 + t20 * t19 * t49 - t49 * t10) * 0.5 * t30 - d3 / two * t60 * t64 * t140;
      dfds[0] = t2 + t41 * t46 / two + t14 * (-t36 * t68 * t84 + t35 * t68 * t84 * t89);
      dfds[1] = t2 + t41 * t97 / two + t14 * (-t36 * t111 * t84 + t35 * t111 * t84 * t89);
      dfds[2] = t41 * txy + t14 * (-t36 * t129 * t84 + t35 * t129 * t84 * t89);
      dfds[3] = t2 + t41 * t140 / two + t14 * (-t36 * t152 * t84 + t35 * t152 * t84 * t89);
      t2 = snps / d3;
      t41 = one / t14 * (t35 + t36 * snps / sq3);
      t89 = t2 / sq3;
      dqds[0] = t2 + t41 * t46 / two + t14 * (-t36 * t68 * t84 + t35 * t68 * t84 * t89);
      dqds[1] = t2 + t41 * t97 / two + t14 * (-t36 * t111 * t84 + t35 * t111 * t84 * t89);
      dqds[2] = t41 * txy + t14 * (-t36 * t129 * t84 + t35 * t129 * t84 * t89);
      dqds[3] = t2 + t41 * t140 / two + t14 * (-t36 * t152 * t84 + t35 * t152 * t84 * t89);
    }
  } else if (ih == 6) {
    std::fprintf(stderr, "mcdpl: 6-component case not fully ported; pl set to zero\n");
    for (int i = 0; i < ih; ++i)
      for (int j = 0; j < ih; ++j)
        pl[i][j] = 0.0;
    return;
  }

  for (int i = 0; i < ih; ++i) {
    ddqds[i] = 0.0;
    dfdsd[i] = 0.0;
    for (int j = 0; j < ih; ++j) {
      ddqds[i] += dee[i][j] * dqds[j];
      dfdsd[i] += dee[i][j] * dfds[j];
    }
  }
  double denom = 0.0;
  for (int i = 0; i < ih; ++i) denom += dfdsd[i] * dqds[i];
  if (std::abs(denom) < 1e-30) denom = 1e-30;
  for (int i = 0; i < ih; ++i)
    for (int j = 0; j < ih; ++j)
      pl[i][j] = ddqds[i] * dfdsd[j] / denom;
}

} // namespace fem
