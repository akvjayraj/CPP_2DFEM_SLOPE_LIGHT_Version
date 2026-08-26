#include "bandred.hpp"
#include <cmath>
#include <algorithm>

namespace fem {

void bandred(const std::vector<std::vector<double>>& ku,
             std::vector<double>& diag,
             std::vector<double>& udiag) {
  const int neq = static_cast<int>(ku.size());
  const int nband = static_cast<int>(ku[0].size()) - 1;
  if (neq <= 0) return;
  diag.resize(neq + 1);
  udiag.resize(neq + 1);
  udiag[0] = 0.0;
  udiag[1] = 0.0;

  std::vector<std::vector<double>> a(neq, std::vector<double>(neq, 0.0));
  for (int i = 0; i < neq; ++i) {
    for (int k = 0; k <= nband && i + k < neq; ++k) {
      int j = i + k;
      double v = ku[i][k];
      a[i][j] = v;
      a[j][i] = v;
    }
  }

  for (int j = 0; j < neq - 2; ++j) {
    for (int i = neq - 1; i >= j + 2; --i) {
      double aij = a[i][j];
      double aim1j = a[i - 1][j];
      double r = std::sqrt(aim1j * aim1j + aij * aij);
      if (r < 1e-30) continue;
      double c = aim1j / r;
      double s = aij / r;
      a[i][j] = 0.0;
      a[j][i] = 0.0;
      a[i - 1][j] = r;
      a[j][i - 1] = r;
      for (int col = j + 1; col < neq; ++col) {
        double t1 = a[i - 1][col];
        double t2 = a[i][col];
        a[i - 1][col] = c * t1 + s * t2;
        a[i][col] = -s * t1 + c * t2;
        a[col][i - 1] = a[i - 1][col];
        a[col][i] = a[i][col];
      }
    }
  }

  for (int i = 0; i < neq; ++i)
    diag[i + 1] = a[i][i];
  for (int i = 0; i < neq - 1; ++i)
    udiag[i + 2] = a[i][i + 1];
}

} // namespace fem
