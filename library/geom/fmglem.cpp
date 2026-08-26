#include "fmglem.hpp"

namespace geom {

void fmglem(int fnxe, int fnye, int lnxe, int lifts,
            std::vector<std::vector<int>>& g_num) {
  int ig = 0;

  for (int i = 0; i < fnye; ++i) {
    for (int j = 0; j < fnxe; ++j) {
      ++ig;
      g_num[0][ig - 1] = (j + 1) * 2 - 1 + i * (fnxe + 1) + i * (2 * fnxe + 1);
      g_num[7][ig - 1] = g_num[0][ig - 1] + 1;
      g_num[6][ig - 1] = g_num[0][ig - 1] + 2;
      g_num[1][ig - 1] = (i + 1) * (2 * fnxe + 1) + j + 1 + i * (fnxe + 1);
      g_num[5][ig - 1] = g_num[1][ig - 1] + 1;
      g_num[2][ig - 1] = (i + 1) * (2 * fnxe + 1) + (i + 1) * (fnxe + 1) + (j + 1) * 2 - 1;
      g_num[3][ig - 1] = g_num[2][ig - 1] + 1;
      g_num[4][ig - 1] = g_num[2][ig - 1] + 2;
    }
  }

  int ilast = g_num[4][ig - 1];

  for (int ii = 0; ii < lifts - 1; ++ii) {
    for (int j = 0; j < lnxe - ii; ++j) {
      ++ig;
      g_num[0][ig - 1] = ilast - (lnxe - ii) * 2 + j * 2;
      g_num[7][ig - 1] = g_num[0][ig - 1] + 1;
      g_num[6][ig - 1] = g_num[0][ig - 1] + 2;
      g_num[1][ig - 1] = ilast + j + 1;
      g_num[5][ig - 1] = g_num[1][ig - 1] + 1;
      g_num[2][ig - 1] = ilast + (lnxe - ii) + 1 + (j + 1) * 2 - 1;
      g_num[3][ig - 1] = g_num[2][ig - 1] + 1;
      g_num[4][ig - 1] = g_num[2][ig - 1] + 2;
    }
    ilast = g_num[4][ig - 1];
  }
}

} // namespace geom
