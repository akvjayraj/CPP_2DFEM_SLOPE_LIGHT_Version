#include "bmat_nonaxi.hpp"

namespace fem {

void bmat_nonaxi(std::vector<std::vector<double>>& bee, double& radius,
                 const std::vector<std::vector<double>>& coord, const std::vector<std::vector<double>>& deriv,
                 const std::vector<double>& fun, int iflag, int lth) {
  int nod = static_cast<int>(deriv[0].size());
  radius = 0.0;
  for (int m = 0; m < nod; ++m) radius += fun[m] * coord[m][0];
  for (size_t i = 0; i < bee.size(); ++i)
    for (size_t j = 0; j < bee[0].size(); ++j) bee[i][j] = 0.0;
  if (radius < 1e-30) radius = 1e-30;
  for (int m = 0; m < nod; ++m) {
    int l = 3 * m, k = 3 * m + 1, n = 3 * m + 2;
    if (n >= static_cast<int>(bee[0].size())) continue;
    bee[0][l] = deriv[0][m];
    bee[1][k] = deriv[1][m];
    bee[2][l] = fun[m] / radius;
    bee[2][n] = iflag * lth * bee[2][l];
    bee[3][l] = deriv[1][m];
    bee[3][k] = deriv[0][m];
    bee[4][k] = -iflag * lth * fun[m] / radius;
    bee[4][n] = deriv[1][m];
    bee[5][l] = bee[4][k];
    bee[5][n] = deriv[0][m] - fun[m] / radius;
  }
}

} // namespace fem
