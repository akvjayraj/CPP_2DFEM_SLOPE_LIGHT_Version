#include "beemat.hpp"

namespace fem {

void beemat(std::vector<std::vector<double>>& bee,
            const std::vector<std::vector<double>>& deriv) {
  int ih = static_cast<int>(bee.size());
  int nod = static_cast<int>(deriv[0].size());

  for (size_t i = 0; i < bee.size(); ++i)
    for (size_t j = 0; j < bee[i].size(); ++j)
      bee[i][j] = 0.0;

  if (ih == 3 || ih == 4) {
    for (int m = 0; m < nod; ++m) {
      int k = 2 * (m + 1);
      int l = k - 1;
      double x = deriv[0][m];
      double y = deriv[1][m];
      bee[0][l - 1] = x;
      bee[2][k - 1] = x;
      bee[1][k - 1] = y;
      bee[2][l - 1] = y;
    }
  } else if (ih == 6) {
    for (int m = 0; m < nod; ++m) {
      int n = 3 * (m + 1);
      int k = n - 1;
      int l = k - 1;
      double x = deriv[0][m];
      double y = deriv[1][m];
      double z = deriv[2][m];
      bee[0][l - 1] = x;
      bee[3][k - 1] = x;
      bee[5][n - 1] = x;
      bee[1][k - 1] = y;
      bee[3][l - 1] = y;
      bee[4][n - 1] = y;
      bee[2][n - 1] = z;
      bee[4][k - 1] = z;
      bee[5][l - 1] = z;
    }
  }
}

} // namespace fem
