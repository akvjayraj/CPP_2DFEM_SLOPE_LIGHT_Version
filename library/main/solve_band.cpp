#include "solve_band.hpp"
#include <cmath>

namespace fem {

void solve_band(const std::vector<std::vector<double>>& pb, const std::vector<std::vector<double>>& work,
                std::vector<double>& loads) {
  const int iwp1 = (static_cast<int>(pb[0].size()) - 1) / 2 + 1;
  const int n = static_cast<int>(pb.size());
  const int iq = 2 * iwp1 - 1;
  const int n1 = n - 1;

  for (int iv = 0; iv < n1; ++iv) {
    int i = static_cast<int>(work[iwp1 - 1][iv] + 0.5);
    if (i != iv) {
      double t = loads[iv];
      loads[iv] = loads[i];
      loads[i] = t;
    }
    int l = iv + iwp1 - 1;
    if (l >= n) l = n - 1;
    for (int i = iv + 1; i <= l; ++i)
      loads[i] -= work[i - iv - 1][iv] * loads[iv];
  }
  loads[n - 1] /= pb[n - 1][0];
  for (int jj = 1; jj < n; ++jj) {
    int iv = n - 1 - jj;
    double s = loads[iv];
    int l = iq;
    if (iv + l - 1 >= n) l = n - iv;
    for (int i = 1; i < l; ++i)
      s -= pb[iv][i] * loads[iv + i];
    loads[iv] = s / pb[iv][0];
  }
}

} // namespace fem
