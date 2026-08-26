#include "interp.hpp"

namespace fem {

void interp(int k, double dtim, const std::vector<double>& rt,
            const std::vector<double>& rl, std::vector<std::vector<double>>& al, int nstep) {
  const int np = static_cast<int>(rt.size());
  if (np == 0 || static_cast<int>(al.size()) < nstep || static_cast<int>(al[0].size()) <= k) return;
  al[0][k] = rl[0];
  double t = rt[0];
  for (int j = 1; j < nstep; ++j) {
    t += dtim;
    double val = rl[np - 1];
    for (int i = 1; i < np; ++i) {
      if (t <= rt[i]) {
        val = rl[i - 1] + (t - rt[i - 1]) / (rt[i] - rt[i - 1]) * (rl[i] - rl[i - 1]);
        break;
      }
    }
    al[j][k] = val;
  }
}

} // namespace fem
