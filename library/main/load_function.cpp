#include "load_function.hpp"

namespace fem {

void load_function(const std::vector<std::vector<double>>& lf, double dtim, std::vector<double>& al) {
  const int nlfp = static_cast<int>(lf[0].size());
  const int nincs = static_cast<int>(al.size());
  if (nlfp == 0 || static_cast<int>(lf.size()) < 2) return;
  double aold = lf[1][0];
  double time = lf[0][0];
  for (int i = 0; i < nincs; ++i) {
    time += dtim;
    al[i] = 0.0;
    for (int j = 1; j < nlfp; ++j) {
      if (time < lf[0][j]) {
        double anew = lf[1][j - 1] + (time - lf[0][j - 1]) * (lf[1][j] - lf[1][j - 1]) / (lf[0][j] - lf[0][j - 1]);
        al[i] = anew - aold;
        aold = anew;
        break;
      }
    }
  }
}

} // namespace fem
