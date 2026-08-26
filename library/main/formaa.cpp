#include "formaa.hpp"

namespace fem {

void formaa(const std::vector<double>& vmfl, const std::vector<std::vector<double>>& rmat,
            std::vector<std::vector<double>>& daatd) {
  std::vector<std::vector<double>> flowa(4, std::vector<double>(1));
  for (int i = 0; i < 4; ++i) flowa[i][0] = vmfl[i];
  std::vector<std::vector<double>> tmp(4, std::vector<double>(1));
  for (int i = 0; i < 4; ++i) {
    tmp[i][0] = 0.0;
    for (int k = 0; k < 4; ++k) tmp[i][0] += rmat[i][k] * flowa[k][0];
  }
  std::vector<std::vector<double>> tmp2(4, std::vector<double>(4));
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      tmp2[i][j] = tmp[i][0] * vmfl[j];
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j) {
      daatd[i][j] = 0.0;
      for (int k = 0; k < 4; ++k) daatd[i][j] += rmat[i][k] * tmp2[k][j];
    }
}

} // namespace fem
