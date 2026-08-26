#include "rect_km.hpp"
#include <stdexcept>

namespace fem {

void rect_km(std::vector<std::vector<double>>& km,
             const std::vector<std::vector<double>>& coord,
             double e, double v) {
  const double d0 = 0.0, d1 = 1.0, d2 = 2.0, d4 = 4.0, d5 = 5.0, d6 = 6.0;
  const double d7 = 7.0, d8 = 8.0, d9 = 9.0, d12 = 12.0, d16 = 16.0;
  const double d17 = 17.0, d18 = 18.0, d24 = 24.0, d72 = 72.0;

  int nod = static_cast<int>(coord.size());

  if (nod == 4) {
    double aa = coord[2][0] - coord[1][0];  // coord(3,1)-coord(2,1)
    double bb = coord[1][1] - coord[0][1];  // coord(2,2)-coord(1,2)
    double t2 = -d1 + d2 * v;
    double t3 = e * t2;
    double t4 = aa * aa;
    double t6 = -d1 + v;
    double t7 = d2 * e * t6;
    double t8 = bb * bb;
    double t10 = t3 * t4 + t7 * t8;
    double t11 = d1 / aa;
    double t15 = d1 / (d1 + v);
    double t17 = d1 / t2;
    double t18 = d1 / bb * t15 * t17;
    double t20 = t10 * t11 * t18 / d6;
    double t23 = e * t15 * t17 / d8;
    double t25 = -e * t2 * t4;
    double t31 = (t25 + e * t6 * t8) * t11 * t18 / d6;
    double t37 = e * (d4 * v - d1) * t15 * t17 / d8;
    double t40 = -t10 * t11 * t18 / d12;
    double t46 = (-t25 - d4 * e * t6 * t8) * t11 * t18 / d12;
    double t48 = t3 * t8;
    double t49 = t7 * t4 + t48;
    double t52 = t49 * t11 * t18 / d6;
    double t58 = (-d4 * e * t6 * t4 + t48) * t11 * t18 / d12;
    double t61 = -t49 * t11 * t18 / d12;
    double t67 = (e * t6 * t4 - t48) * t11 * t18 / d6;

    km[0][0] = t20; km[0][1] = -t23; km[0][2] = t31; km[0][3] = t37;
    km[0][4] = t40; km[0][5] = t23; km[0][6] = t46; km[0][7] = -t37;
    km[1][1] = t52; km[1][2] = -t37; km[1][3] = t58; km[1][4] = t23;
    km[1][5] = t61; km[1][6] = t37; km[1][7] = t67;
    km[2][2] = t20; km[2][3] = t23; km[2][4] = t46; km[2][5] = t37;
    km[2][6] = t40; km[2][7] = -t23;
    km[3][3] = t52; km[3][4] = -t37; km[3][5] = t67; km[3][6] = -t23; km[3][7] = t61;
    km[4][4] = t20; km[4][5] = -t23; km[4][6] = t31; km[4][7] = t37;
    km[5][5] = t52; km[5][6] = -t37; km[5][7] = t58;
    km[6][6] = t20; km[6][7] = t23;
    km[7][7] = t52;
  } else if (nod == 8) {
    double aa = coord[4][0] - coord[2][0];
    double bb = coord[2][1] - coord[0][1];
    double t2 = -d1 + d2 * v;
    double t3 = e * t2;
    double t4 = aa * aa;
    double t5 = t3 * t4;
    double t6 = -d1 + v;
    double t7 = d2 * e * t6;
    double t8 = bb * bb;
    double t11 = d1 / aa;
    double t15 = d1 / (d1 + v);
    double t17 = d1 / t2;
    double t18 = d1 / bb * t15 * t17;
    double t19 = d5 * (t5 + t7 * t8) * t11 * t18;
    double t20 = t19 / d18;
    double t22 = e * t15 * t17;
    double t23 = d17 / d72 * t22;
    double t25 = -d4 + d8 * v;
    double t29 = -e * t6 * t8;
    double t33 = (-e * t25 * t4 - t29) * t11 * t18 / d9;
    double t34 = d12 * v;
    double t37 = t15 * t17;
    double t39 = e * (t34 - d1) * t37 / d18;
    double t40 = e * t6;
    double t41 = t40 * t8;
    double t45 = (t5 + t41) * t11 * t18 / d6;
    double t46 = d4 * v;
    double t50 = e * (t46 - d1) * t37 / d24;
    double t51 = d8 * e * t6;
    double t53 = -t5 - t51 * t8;
    double t56 = t53 * t11 * t18 / d18;
    double t57 = t22 / d18;
    double t58 = t19 / 36.0;
    double t59 = d7 / d72 * t22;
    double t61 = e * (-d2 + t46);
    double t63 = -t61 * t4 - t41;
    double t66 = t63 * t11 * t18 / d9;
    double t67 = d4 * e * t6;
    double t72 = (t5 + t67 * t8) * t11 * t18 / d12;
    double t78 = (t5 - d16 * e * t6 * t8) * t11 * t18 / d18;
    double t82 = e * (t34 - d5) * t37 / d18;
    double t84 = t3 * t8;
    double t87 = d5 * (t7 * t4 + t84) * t11 * t18;
    double t88 = t87 / d18;
    double t92 = -e * t2 * t8;
    double t96 = (-d16 * e * t6 * t4 - t92) * t11 * t18 / d18;
    double t97 = t67 * t4;
    double t101 = (t97 + t84) * t11 * t18 / d12;
    double t102 = t40 * t4;
    double t104 = -t102 - t61 * t8;
    double t107 = t104 * t11 * t18 / d9;
    double t108 = t87 / 36.0;
    double t110 = -t51 * t4 - t84;
    double t113 = t110 * t11 * t18 / d18;
    double t117 = (t102 + t84) * t11 * t18 / d6;
    double t123 = (t102 - e * t25 * t8) * t11 * t18 / d9;
    double t126 = -d4 / d9 * t63 * t11 * t18;
    double t127 = d2 / d9 * t22;
    double t134 = -d2 / d9 * t110 * t11 * t18;
    double t141 = -d2 / d9 * t53 * t11 * t18;
    double t150 = -d4 / d9 * t104 * t11 * t18;

    km[0][0] = t20; km[0][1] = -t23; km[0][2] = t33; km[0][3] = t39;
    km[0][4] = t45; km[0][5] = -t50; km[0][6] = t56; km[0][7] = t57;
    km[0][8] = t58; km[0][9] = -t59; km[0][10] = t66; km[0][11] = t57;
    km[0][12] = t72; km[0][13] = t50; km[0][14] = t78; km[0][15] = -t82;
    km[1][1] = t88; km[1][2] = -t82; km[1][3] = t96; km[1][4] = t50;
    km[1][5] = t101; km[1][6] = t57; km[1][7] = t107; km[1][8] = -t59;
    km[1][9] = t108; km[1][10] = t57; km[1][11] = t113; km[1][12] = -t50;
    km[1][13] = t117; km[1][14] = t39; km[1][15] = t123;
    km[2][2] = t126; km[2][3] = d0; km[2][4] = t33; km[2][5] = t82;
    km[2][6] = d0; km[2][7] = t127; km[2][8] = t66; km[2][9] = t57;
    km[2][10] = d4 / d9 * (t5 + t29) * t11 * t18; km[2][11] = d0;
    km[2][12] = t66; km[2][13] = -t57; km[2][14] = d0; km[2][15] = -t127;
    km[3][3] = t134; km[3][4] = -t39; km[3][5] = t96; km[3][6] = t127;
    km[3][7] = d0; km[3][8] = t57; km[3][9] = t113; km[3][10] = d0;
    km[3][11] = d2 / d9 * (t97 + t92) * t11 * t18;
    km[3][12] = -t57; km[3][13] = t113; km[3][14] = -t127; km[3][15] = d0;
    km[4][4] = t20; km[4][5] = t23; km[4][6] = t78; km[4][7] = t82;
    km[4][8] = t72; km[4][9] = -t50; km[4][10] = t66; km[4][11] = -t57;
    km[4][12] = t58; km[4][13] = t59; km[4][14] = t56; km[4][15] = -t57;
    km[5][5] = t88; km[5][6] = -t39; km[5][7] = t123; km[5][8] = t50;
    km[5][9] = t117; km[5][10] = -t57; km[5][11] = t113;
    km[5][12] = t59; km[5][13] = t108; km[5][14] = -t57; km[5][15] = t107;
    km[6][6] = t141; km[6][7] = d0; km[6][8] = t78; km[6][9] = t39;
    km[6][10] = d0; km[6][11] = -t127; km[6][12] = t56; km[6][13] = -t57;
    km[6][14] = d2 / d9 * (-t5 + d4 * e * t6 * t8) * t11 * t18; km[6][15] = d0;
    km[7][7] = t150; km[7][8] = -t82; km[7][9] = t123; km[7][10] = -t127;
    km[7][11] = d0; km[7][12] = -t57; km[7][13] = t107; km[7][14] = d0;
    km[7][15] = d4 / d9 * (-t102 - t92) * t11 * t18;
    km[8][8] = t20; km[8][9] = -t23; km[8][10] = t33; km[8][11] = t39;
    km[8][12] = t45; km[8][13] = -t50; km[8][14] = t56; km[8][15] = t57;
    km[9][9] = t88; km[9][10] = -t82; km[9][11] = t96;
    km[9][12] = t50; km[9][13] = t101; km[9][14] = t57; km[9][15] = t107;
    km[10][10] = t126; km[10][11] = d0; km[10][12] = t33; km[10][13] = t82;
    km[10][14] = d0; km[10][15] = t127;
    km[11][11] = t134; km[11][12] = -t39; km[11][13] = t96;
    km[11][14] = t127; km[11][15] = d0;
    km[12][12] = t20; km[12][13] = t23; km[12][14] = t78; km[12][15] = t82;
    km[13][13] = t88; km[13][14] = -t39; km[13][15] = t123;
    km[14][14] = t141; km[14][15] = d0;
    km[15][15] = t150;
  } else {
    throw std::runtime_error("Wrong number of nodes for rectangular element");
  }

  for (int i = 0; i < nod * 2; ++i) {
    for (int j = i + 1; j < nod * 2; ++j) {
      km[j][i] = km[i][j];
    }
  }
}

} // namespace fem
