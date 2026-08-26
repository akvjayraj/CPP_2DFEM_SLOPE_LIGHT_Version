#include "formm.hpp"
#include <cstdio>

namespace fem {

static const double one = 1.0, two = 2.0, three = 3.0, six = 6.0, nine = 9.0;

void formm(const std::vector<double>& stress,
           std::vector<std::vector<double>>& m1,
           std::vector<std::vector<double>>& m2,
           std::vector<std::vector<double>>& m3) {
  const int nst = static_cast<int>(stress.size());
  const int ih = (nst == 4) ? 4 : 6;
  for (int i = 0; i < ih; ++i)
    for (int j = 0; j < ih; ++j)
      m1[i][j] = m2[i][j] = m3[i][j] = 0.0;

  if (nst == 4) {
    double sx = stress[0], sy = stress[1], txy = stress[2], sz = stress[3];
    double dx = (two * sx - sy - sz) / three;
    double dy = (two * sy - sz - sx) / three;
    double dz = (two * sz - sx - sy) / three;
    double sigm = (sx + sy + sz) / three;
    m1[0][0] = m1[0][1] = m1[1][0] = m1[1][1] = one / nine / sigm;
    m1[3][0] = m1[3][3] = m1[0][3] = m1[1][3] = one / nine / sigm;
    m2[0][0] = two / three; m2[1][1] = two / three; m2[3][3] = two / three;
    m2[1][3] = m2[3][1] = -one / three;
    m2[0][1] = m2[1][0] = m2[0][3] = m2[3][0] = -one / three;
    m2[2][2] = two;
    m3[2][2] = -dz;
    m3[0][2] = m3[2][0] = m3[1][2] = m3[2][1] = txy / three;
    m3[2][3] = m3[3][2] = -two * txy / three;
    m3[0][0] = dx / three; m3[1][3] = m3[3][1] = dx / three;
    m3[1][1] = dy / three; m3[0][3] = m3[3][0] = dy / three;
    m3[3][3] = dz / three; m3[0][1] = m3[1][0] = dz / three;
    return;
  }
  if (nst == 6) {
    double sx = stress[0], sy = stress[1], sz = stress[2];
    double txy = stress[3], tyz = stress[4], tzx = stress[5];
    double sigm = (sx + sy + sz) / three;
    double dx = sx - sigm, dy = sy - sigm, dz = sz - sigm;
    for (int i = 0; i < 3; ++i)
      for (int j = 0; j < 3; ++j)
        m1[i][j] = one / (nine * sigm);
    for (int i = 0; i < 3; ++i) m2[i][i] = two / three;
    for (int i = 3; i < 6; ++i) m2[i][i] = six / three;
    m2[0][1] = m2[1][0] = m2[0][2] = m2[2][0] = m2[1][2] = m2[2][1] = -one / three;
    m3[0][0] = dx; m3[0][1] = dz; m3[0][2] = dy; m3[0][3] = txy; m3[0][4] = -two * tyz; m3[0][5] = tzx;
    m3[1][1] = dy; m3[1][2] = dx; m3[1][3] = txy; m3[1][4] = tyz; m3[1][5] = -two * tzx;
    m3[2][2] = dz; m3[2][3] = -two * txy; m3[2][4] = tyz; m3[2][5] = tzx;
    m3[3][3] = -three * dz; m3[3][4] = three * tzx; m3[3][5] = three * tyz;
    m3[4][4] = -three * dx; m3[4][5] = three * txy;
    m3[5][5] = -three * dy;
    for (int i = 0; i < 6; ++i)
      for (int j = i + 1; j < 6; ++j) {
        m1[j][i] = m1[i][j];
        m2[j][i] = m2[i][j];
        m3[j][i] = m3[i][j];
      }
    return;
  }
  std::fprintf(stderr, "nst size not recognised in formm\n");
}

} // namespace fem
