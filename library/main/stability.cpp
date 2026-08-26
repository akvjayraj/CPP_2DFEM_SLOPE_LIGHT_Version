#include "stability.hpp"
#include "linmul_sky.hpp"
#include "sparin.hpp"
#include "spabac.hpp"
#include <cmath>
#include <algorithm>

namespace fem {

void stability(std::vector<double>& kv, std::vector<double>& gv,
               const std::vector<int>& kdiag, double tol, int limit,
               int& iters, std::vector<double>& evec, double& eval) {
  int neq = static_cast<int>(kdiag.size());
  std::vector<double> x0(neq + 1, 0.0), x1(neq + 1, 0.0);
  sparin(kv, kdiag);
  iters = 0;
  x0[1] = 1.0;
  double big = 0.0;
  for (;;) {
    ++iters;
    linmul_sky(gv, x0, x1, kdiag);
    spabac(kv, x1, kdiag);
    big = 0.0;
    for (int i = 1; i <= neq; ++i) {
      double ax = std::abs(x1[i]);
      if (ax > big) big = ax;
    }
    for (int i = 1; i <= neq; ++i) x1[i] /= big;
    bool converged = true;
    double max_x1 = 0.0;
    for (int i = 1; i <= neq; ++i)
      if (std::abs(x1[i]) > max_x1) max_x1 = std::abs(x1[i]);
    if (max_x1 < 1e-30) max_x1 = 1e-30;
    for (int i = 1; i <= neq; ++i)
      if (std::abs(x1[i] - x0[i]) / max_x1 >= tol) { converged = false; break; }
    x0 = x1;
    if (converged || iters == limit) break;
  }
  double sum = 0.0;
  for (int i = 1; i <= neq; ++i) sum += x1[i] * x1[i];
  double norm = std::sqrt(sum);
  for (int i = 1; i <= neq; ++i) x1[i] /= norm;
  evec = x1;
  evec[0] = 0.0;
  eval = (big > 1e-30) ? (1.0 / big) : 0.0;
}

} // namespace fem
