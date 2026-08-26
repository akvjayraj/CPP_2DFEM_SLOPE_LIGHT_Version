#include "fmcoem.hpp"

namespace geom {

void fmcoem(const std::vector<std::vector<int>>& g_num,
            std::vector<std::vector<double>>& g_coord,
            const std::vector<double>& fwidth, const std::vector<double>& fdepth,
            const std::vector<double>& width, const std::vector<double>& depth,
            int lnxe, int lifts, int fnxe, int fnye, int itype) {
  const double pt5 = 0.5;
  int lnye = 1;
  int ig = 0;

  for (int i = 0; i < fnye; ++i) {
    for (int j = 0; j < fnxe; ++j) {
      ++ig;
      int n1 = g_num[0][ig - 1], n2 = g_num[1][ig - 1], n3 = g_num[2][ig - 1];
      int n4 = g_num[3][ig - 1], n5 = g_num[4][ig - 1], n6 = g_num[5][ig - 1];
      int n7 = g_num[6][ig - 1], n8 = g_num[7][ig - 1];

      g_coord[0][n1 - 1] = g_coord[0][n2 - 1] = g_coord[0][n3 - 1] = fwidth[j];
      g_coord[0][n5 - 1] = g_coord[0][n6 - 1] = g_coord[0][n7 - 1] = fwidth[j + 1];
      g_coord[0][n4 - 1] = pt5 * (g_coord[0][n3 - 1] + g_coord[0][n5 - 1]);
      g_coord[0][n8 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n7 - 1]);

      g_coord[1][n1 - 1] = g_coord[1][n8 - 1] = g_coord[1][n7 - 1] = fdepth[i];
      g_coord[1][n3 - 1] = g_coord[1][n4 - 1] = g_coord[1][n5 - 1] = fdepth[i + 1];
      g_coord[1][n2 - 1] = pt5 * (g_coord[1][n1 - 1] + g_coord[1][n3 - 1]);
      g_coord[1][n6 - 1] = pt5 * (g_coord[1][n7 - 1] + g_coord[1][n5 - 1]);
    }
  }

  if (itype == 1) {
    for (int ii = 0; ii < lifts - 1; ++ii) {
      for (int i = 0; i < lnye; ++i) {
        for (int j = 0; j < lnxe - ii; ++j) {
          ++ig;
          int n1 = g_num[0][ig - 1], n2 = g_num[1][ig - 1], n3 = g_num[2][ig - 1];
          int n4 = g_num[3][ig - 1], n5 = g_num[4][ig - 1], n6 = g_num[5][ig - 1];
          int n7 = g_num[6][ig - 1], n8 = g_num[7][ig - 1];

          if (j == 0) {
            g_coord[0][n1 - 1] = width[ii + j];
            g_coord[0][n5 - 1] = width[ii + j + 1];
            g_coord[0][n6 - 1] = g_coord[0][n7 - 1] = g_coord[0][n5 - 1];
            g_coord[0][n3 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n5 - 1]);
            g_coord[0][n2 - 1] = pt5 * (g_coord[0][n3 - 1] + g_coord[0][n1 - 1]);
            g_coord[0][n4 - 1] = pt5 * (g_coord[0][n3 - 1] + g_coord[0][n5 - 1]);
            g_coord[0][n8 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n7 - 1]);
            g_coord[1][n1 - 1] = g_coord[1][n8 - 1] = g_coord[1][n7 - 1] = depth[ii + i];
            g_coord[1][n5 - 1] = depth[ii + i + 1];
            g_coord[1][n3 - 1] = pt5 * (g_coord[1][n1 - 1] + g_coord[1][n5 - 1]);
            g_coord[1][n2 - 1] = pt5 * (g_coord[1][n1 - 1] + g_coord[1][n3 - 1]);
            g_coord[1][n4 - 1] = pt5 * (g_coord[1][n3 - 1] + g_coord[1][n5 - 1]);
            g_coord[1][n6 - 1] = pt5 * (g_coord[1][n5 - 1] + g_coord[1][n7 - 1]);
          } else {
            g_coord[0][n1 - 1] = g_coord[0][n2 - 1] = g_coord[0][n3 - 1] = width[ii + j];
            g_coord[0][n5 - 1] = g_coord[0][n6 - 1] = g_coord[0][n7 - 1] = width[ii + j + 1];
            g_coord[0][n4 - 1] = pt5 * (g_coord[0][n3 - 1] + g_coord[0][n5 - 1]);
            g_coord[0][n8 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n7 - 1]);
            g_coord[1][n1 - 1] = g_coord[1][n8 - 1] = g_coord[1][n7 - 1] = depth[ii + i];
            g_coord[1][n3 - 1] = g_coord[1][n4 - 1] = g_coord[1][n5 - 1] = depth[ii + i + 1];
            g_coord[1][n2 - 1] = pt5 * (g_coord[1][n1 - 1] + g_coord[1][n3 - 1]);
            g_coord[1][n6 - 1] = pt5 * (g_coord[1][n5 - 1] + g_coord[1][n7 - 1]);
          }
        }
      }
    }
  } else {
    for (int ii = 0; ii < lifts - 1; ++ii) {
      for (int i = 0; i < lnye; ++i) {
        for (int j = 0; j < lnxe - ii; ++j) {
          ++ig;
          int n1 = g_num[0][ig - 1], n2 = g_num[1][ig - 1], n3 = g_num[2][ig - 1];
          int n4 = g_num[3][ig - 1], n5 = g_num[4][ig - 1], n6 = g_num[5][ig - 1];
          int n7 = g_num[6][ig - 1], n8 = g_num[7][ig - 1];

          if (j == 0) {
            g_coord[0][n1 - 1] = width[ii + j];
            g_coord[0][n5 - 1] = width[ii + j + 1];
            g_coord[0][n6 - 1] = g_coord[0][n7 - 1] = g_coord[0][n5 - 1];
            g_coord[0][n3 - 1] = g_coord[0][n4 - 1] = g_coord[0][n5 - 1];
            g_coord[0][n8 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n7 - 1]);
            g_coord[0][n2 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n5 - 1]);
            g_coord[1][n1 - 1] = g_coord[1][n8 - 1] = g_coord[1][n7 - 1] = depth[ii + i];
            g_coord[1][n5 - 1] = depth[ii + i + 1];
            g_coord[1][n2 - 1] = pt5 * (g_coord[1][n1 - 1] + g_coord[1][n5 - 1]);
            g_coord[1][n6 - 1] = pt5 * (g_coord[1][n5 - 1] + g_coord[1][n7 - 1]);
            g_coord[1][n3 - 1] = g_coord[1][n4 - 1] = g_coord[1][n5 - 1];
          } else {
            g_coord[0][n1 - 1] = g_coord[0][n2 - 1] = g_coord[0][n3 - 1] = width[ii + j];
            g_coord[0][n5 - 1] = g_coord[0][n6 - 1] = g_coord[0][n7 - 1] = width[ii + j + 1];
            g_coord[0][n4 - 1] = pt5 * (g_coord[0][n3 - 1] + g_coord[0][n5 - 1]);
            g_coord[0][n8 - 1] = pt5 * (g_coord[0][n1 - 1] + g_coord[0][n7 - 1]);
            g_coord[1][n1 - 1] = g_coord[1][n8 - 1] = g_coord[1][n7 - 1] = depth[ii + i];
            g_coord[1][n3 - 1] = g_coord[1][n4 - 1] = g_coord[1][n5 - 1] = depth[ii + i + 1];
            g_coord[1][n2 - 1] = pt5 * (g_coord[1][n1 - 1] + g_coord[1][n3 - 1]);
            g_coord[1][n6 - 1] = pt5 * (g_coord[1][n5 - 1] + g_coord[1][n7 - 1]);
          }
        }
      }
    }
  }
}

} // namespace geom
