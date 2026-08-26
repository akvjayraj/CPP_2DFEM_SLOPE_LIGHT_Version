#include "determinant.hpp"

namespace fem {

double determinant(const std::vector<std::vector<double>>& jac) {
  int n = static_cast<int>(jac.size());
  if (n == 1) return jac[0][0];
  if (n == 2)
    return jac[0][0] * jac[1][1] - jac[0][1] * jac[1][0];
  if (n == 3) {
    double det = jac[0][0] * (jac[1][1] * jac[2][2] - jac[2][1] * jac[1][2]);
    det -= jac[0][1] * (jac[1][0] * jac[2][2] - jac[2][0] * jac[1][2]);
    det += jac[0][2] * (jac[1][0] * jac[2][1] - jac[2][0] * jac[1][1]);
    return det;
  }
  return 0.0;
}

} // namespace fem
