#include "fmrmat.hpp"

namespace fem {

void fmrmat(const std::vector<double>& vmfl, double dsbar, double dlam,
            const std::vector<std::vector<double>>& dee, std::vector<std::vector<double>>& rmat) {
  double acat[4][4], acatc[4][4], qmat[4][4], temp[4][4];
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j) temp[i][j] = 0.0;
  temp[0][0] = 1.0; temp[0][1] = temp[0][3] = -0.5;
  temp[1][0] = -0.5; temp[1][1] = 1.0; temp[1][3] = -0.5;
  temp[2][2] = 3.0;
  temp[3][0] = temp[3][1] = -0.5; temp[3][3] = 1.0;
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      acat[i][j] = vmfl[i] * vmfl[j];
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      acat[i][j] = (temp[i][j] - acat[i][j]) / dsbar;
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j) {
      acatc[i][j] = 0.0;
      for (int k = 0; k < 4; ++k) acatc[i][j] += dee[i][k] * acat[k][j];
    }
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j) {
      qmat[i][j] = acatc[i][j] * dlam;
      if (i == j) qmat[i][j] += 1.0;
    }
  for (int i = 0; i < 4; ++i) {
    double con = qmat[i][i];
    qmat[i][i] = 1.0;
    for (int j = 0; j < 4; ++j) qmat[i][j] /= con;
    for (int j = 0; j < 4; ++j) {
      if (j == i) continue;
      con = qmat[j][i];
      qmat[j][i] = 0.0;
      for (int k = 0; k < 4; ++k) qmat[j][k] -= qmat[i][k] * con;
    }
  }
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j) {
      rmat[i][j] = 0.0;
      for (int k = 0; k < 4; ++k) rmat[i][j] += qmat[i][k] * dee[k][j];
    }
}

} // namespace fem
