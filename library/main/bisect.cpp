#include "bisect.hpp"
#include <cmath>
#include <algorithm>
#include <vector>

namespace fem {

namespace {
int sturm_count(int neq, const std::vector<double>& diag, const std::vector<double>& udiag, double sigma) {
  int count = 0;
  double p0 = 1.0;
  double p1 = diag[1] - sigma;
  if (p0 * p1 < 0.0) count++;
  for (int i = 2; i <= neq; ++i) {
    double p2 = (diag[i] - sigma) * p1 - udiag[i] * udiag[i] * p0;
    if (p1 * p2 < 0.0) count++;
    if (p1 == 0.0 && p2 != 0.0) count++;
    p0 = p1;
    p1 = p2;
  }
  return count;
}
}

void bisect(std::vector<double>& diag,
            const std::vector<double>& udiag,
            double etol,
            int& ifail) {
  ifail = 0;
  int neq = static_cast<int>(diag.size()) - 1;
  if (neq <= 0) return;
  double gersh_lo = diag[1];
  double gersh_hi = diag[1];
  for (int i = 1; i <= neq; ++i) {
    double r = std::abs(diag[i]);
    if (i > 1) r += std::abs(udiag[i]);
    if (i < neq) r += std::abs(udiag[i + 1]);
    gersh_lo = std::min(gersh_lo, diag[i] - r);
    gersh_hi = std::max(gersh_hi, diag[i] + r);
  }
  std::vector<double> evals(neq);
  for (int k = 0; k < neq; ++k) {
    double lo = (k == 0) ? gersh_lo : evals[k - 1] + etol * (std::abs(evals[k - 1]) + 1.0);
    double hi = gersh_hi;
    while (sturm_count(neq, diag, udiag, hi) > k + 1) {
      hi = 0.5 * (lo + hi);
      if (hi <= lo) { hi = gersh_hi; break; }
    }
    for (int it = 0; it < 150; ++it) {
      if (hi - lo < etol * (std::abs(lo) + std::abs(hi) + 1.0)) break;
      double mid = 0.5 * (lo + hi);
      int c_mid = sturm_count(neq, diag, udiag, mid);
      if (c_mid <= k) lo = mid;
      else hi = mid;
    }
    evals[k] = 0.5 * (lo + hi);
  }
  for (int i = 0; i < neq; ++i) diag[i + 1] = evals[i];
}

} // namespace fem
