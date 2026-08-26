#include "fmkdke.hpp"

namespace fem {

void fmkdke(const std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& kp,
            const std::vector<std::vector<double>>& c, double theta,
            std::vector<std::vector<double>>& ke, std::vector<std::vector<double>>& kd) {
  int ndof = static_cast<int>(km.size());
  int nc = static_cast<int>(c[0].size());
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < ndof; ++j) ke[i][j] = theta * km[i][j];
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < nc; ++j) ke[i][ndof + j] = theta * c[i][j];
  for (int i = 0; i < nc; ++i)
    for (int j = 0; j < ndof; ++j) ke[ndof + i][j] = theta * c[j][i];
  for (int i = 0; i < nc; ++i)
    for (int j = 0; j < nc; ++j) ke[ndof + i][ndof + j] = -theta * theta * kp[i][j];
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < ndof; ++j) kd[i][j] = (theta - 1.0) * km[i][j];
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < nc; ++j) kd[i][ndof + j] = (theta - 1.0) * c[i][j];
  for (int i = 0; i < nc; ++i)
    for (int j = 0; j < ndof; ++j) kd[ndof + i][j] = ke[ndof + i][j];
  for (int i = 0; i < nc; ++i)
    for (int j = 0; j < nc; ++j) kd[ndof + i][ndof + j] = theta * (1.0 - theta) * kp[i][j];
}

} // namespace fem
