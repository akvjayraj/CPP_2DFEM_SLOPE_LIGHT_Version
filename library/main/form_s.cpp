#include "form_s.hpp"
#include "invert.hpp"
#include <cmath>

namespace fem {

void form_s(const std::vector<std::vector<double>>& gg, int ell, double kappa,
            double& omega, std::vector<double>& gamma, std::vector<double>& s) {
  if (ell < 2) return;
  std::vector<std::vector<double>> hh(ell - 1, std::vector<double>(ell - 1));
  for (int i = 0; i < ell - 1; ++i)
    for (int j = 0; j < ell - 1; ++j)
      hh[i][j] = -gg[i + 1][j + 1];
  if (ell - 1 == 1) {
    if (std::abs(hh[0][0]) < 1e-30) return;
    hh[0][0] = 1.0 / hh[0][0];
  } else {
    fem::invert(hh);
  }
  std::vector<double> p(ell - 1), q(ell - 1);
  for (int i = 0; i < ell - 1; ++i) {
    p[i] = 0.0; q[i] = 0.0;
    for (int j = 0; j < ell - 1; ++j) {
      p[i] += hh[i][j] * gg[j + 1][0];
      q[i] += hh[i][j] * gg[j + 1][ell];
    }
  }
  std::vector<double> gamma0(ell + 1), gamma1(ell + 1);
  gamma0[0] = 1.0; gamma0[ell] = 0.0;
  for (int i = 0; i < ell - 1; ++i) gamma0[i + 1] = p[i];
  gamma1[0] = 0.0; gamma1[ell] = 1.0;
  for (int i = 0; i < ell - 1; ++i) gamma1[i + 1] = q[i];
  double ngamma0 = 0.0, ngamma1 = 0.0;
  for (int i = 0; i <= ell; ++i) {
    double gg_g0 = 0.0, gg_g1 = 0.0;
    for (int j = 0; j <= ell; ++j) {
      gg_g0 += gg[i][j] * gamma0[j];
      gg_g1 += gg[i][j] * gamma1[j];
    }
    ngamma0 += gamma0[i] * gg_g0;
    ngamma1 += gamma1[i] * gg_g1;
  }
  omega = 0.0;
  for (int i = 0; i <= ell; ++i) {
    double gg_g1 = 0.0;
    for (int j = 0; j <= ell; ++j) gg_g1 += gg[i][j] * gamma1[j];
    omega += gamma0[i] * gg_g1;
  }
  double cosine = std::abs(omega) / std::sqrt(std::abs(ngamma0 * ngamma1));
  omega /= ngamma1;
  if (cosine < kappa) omega = (kappa / cosine) * omega;
  for (int i = 0; i <= ell; ++i) gamma[i] = gamma0[i] - omega * gamma1[i];
  for (int i = 0; i < ell; ++i) s[i] = gamma[i + 1];
  s[ell] = 0.0;
}

} // namespace fem
