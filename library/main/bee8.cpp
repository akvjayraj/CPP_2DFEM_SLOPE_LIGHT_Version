#include "bee8.hpp"
#include "beemat.hpp"
#include "shape_der.hpp"
#include <cmath>
#include <vector>

namespace fem {

void bee8(std::vector<std::vector<double>>& bee,
          const std::vector<std::vector<double>>& coord,
          double xi, double eta, double& det) {
  std::vector<std::vector<double>> points = {{xi, eta}};
  std::vector<std::vector<double>> der(2, std::vector<double>(8));
  shape_der(der, points, 1);

  double jac[2][2] = {{0.0, 0.0}, {0.0, 0.0}};
  for (int j = 0; j < 2; ++j)
    for (int k = 0; k < 2; ++k)
      for (int m = 0; m < 8; ++m)
        jac[j][k] += der[j][m] * coord[k][m];

  det = jac[0][0] * jac[1][1] - jac[0][1] * jac[1][0];
  if (std::abs(det) < 1e-20)
    det = (det >= 0.0) ? 1e-20 : -1e-20;

  double inv00 = jac[1][1] / det;
  double inv01 = -jac[0][1] / det;
  double inv10 = -jac[1][0] / det;
  double inv11 = jac[0][0] / det;

  std::vector<std::vector<double>> deriv(2, std::vector<double>(8));
  for (int m = 0; m < 8; ++m) {
    deriv[0][m] = inv00 * der[0][m] + inv01 * der[1][m];
    deriv[1][m] = inv10 * der[0][m] + inv11 * der[1][m];
  }

  bee.resize(4, std::vector<double>(16));
  beemat(bee, deriv);
}

} // namespace fem
