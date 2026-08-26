#include "fmacat.hpp"

namespace fem {

void fmacat(const std::vector<double>& vmfl, std::vector<std::vector<double>>& acat) {
  double temp[4][4] = {{1.0,-0.5,0,-0.5},{-0.5,1.0,0,-0.5},{0,0,3.0,0},{-0.5,-0.5,0,1.0}};
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      acat[i][j] = temp[i][j] - vmfl[i] * vmfl[j];
}

} // namespace fem
