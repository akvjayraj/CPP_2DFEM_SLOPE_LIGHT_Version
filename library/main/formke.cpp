#include "formke.hpp"

namespace fem {

void formke(const std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& kp,
            const std::vector<std::vector<double>>& c, double theta, std::vector<std::vector<double>>& ke) {
  const int ndof = static_cast<int>(km.size());
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < ndof; ++j)
      ke[i][j] = km[i][j];
  const int nc = static_cast<int>(c[0].size());
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < nc; ++j)
      ke[i][ndof + j] = c[i][j];
  for (int i = 0; i < nc; ++i)
    for (int j = 0; j < ndof; ++j)
      ke[ndof + i][j] = c[j][i];
  for (int i = 0; i < nc; ++i)
    for (int j = 0; j < nc; ++j)
      ke[ndof + i][ndof + j] = -theta * kp[i][j];
}

} // namespace fem
