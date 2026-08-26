#include "formupv.hpp"

namespace fem {

void formupv(std::vector<std::vector<double>>& ke,
             const std::vector<std::vector<double>>& c11, const std::vector<std::vector<double>>& c12,
             const std::vector<std::vector<double>>& c21, const std::vector<std::vector<double>>& c23,
             const std::vector<std::vector<double>>& c32) {
  const int nod = static_cast<int>(c11.size());
  const int nodf = static_cast<int>(c21.size());
  const int ntot = nod + nodf + nod;
  for (int i = 0; i < nod; ++i)
    for (int j = 0; j < nod; ++j) ke[i][j] = c11[i][j];
  for (int i = 0; i < nod; ++i)
    for (int j = 0; j < nodf; ++j) ke[i][nod + j] = c12[i][j];
  for (int i = 0; i < nodf; ++i)
    for (int j = 0; j < nod; ++j) ke[nod + i][j] = c21[i][j];
  for (int i = 0; i < nodf; ++i)
    for (int j = 0; j < nodf; ++j) ke[nod + i][nod + nodf + j] = 0.0;
  for (int i = 0; i < nodf; ++i)
    for (int j = 0; j < static_cast<int>(c23[0].size()); ++j) ke[nod + i][nod + nodf + j] = c23[i][j];
  for (int i = 0; i < nod; ++i)
    for (int j = 0; j < nodf; ++j) ke[nod + nodf + i][nod + j] = c32[i][j];
  for (int i = 0; i < nod; ++i)
    for (int j = 0; j < nod; ++j) ke[nod + nodf + i][nod + nodf + j] = c11[i][j];
}

} // namespace fem
