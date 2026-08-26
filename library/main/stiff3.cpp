#include "stiff3.hpp"

namespace fem {

void stiff3(std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& coord,
            double ym, double pr) {
  const double d1 = 1.0, d2 = 2.0, d3 = 3.0, d4 = 4.0, d5 = 5.0, d6 = 6.0;

  double x1 = coord[0][0], y1 = coord[0][1];
  double x2 = coord[1][0], y2 = coord[1][1];
  double x3 = coord[2][0], y3 = coord[2][1];

  double d = -x1 * y2 + x1 * y3 - x2 * y3 + y1 * x2 - y1 * x3 + y2 * x3;
  double cf = ym / (d1 + pr) / (-d1 + d2 * pr) / d / d4;

  km[0][0] = (-d2 * y2 * y2 + d4 * y2 * y3 - d2 * y3 * y3 + d2 * pr * y2 * y2
              - d4 * pr * y2 * y3 + d2 * pr * y3 * y3 - x2 * x2 + d2 * x2 * x2 * pr
              + d2 * x2 * x3 - d4 * x2 * x3 * pr - x3 * x3 + d2 * x3 * x3 * pr) * cf;
  km[0][1] = (x2 - x3) * (y2 - y3) * cf;
  km[0][2] = -(d2 * y2 * y3 - d2 * y2 * y1 - d2 * y3 * y3 + d2 * y3 * y1 - d2 * pr * y2 * y3
               + d2 * pr * y2 * y1 + d2 * pr * y3 * y3 - d2 * pr * y3 * y1 - x2 * x1
               + d2 * x2 * x1 * pr + x2 * x3 - d2 * x2 * x3 * pr + x3 * x1 - d2 * x3 * x1 * pr
               - x3 * x3 + d2 * x3 * x3 * pr) * cf;
  km[0][3] = -(d2 * pr * x1 * y2 - d2 * pr * x1 * y3 - d2 * pr * y2 * x3 - x2 * y3
               + d2 * x2 * y3 * pr + y1 * x2 - d2 * y1 * x2 * pr + y3 * x3 - y1 * x3
               + d2 * y1 * x3 * pr) * cf;
  km[0][4] = -(d2 * y2 * y1 - d2 * y2 * y2 - d2 * y3 * y1 + d2 * y2 * y3 - d2 * pr * y2 * y1
               + d2 * pr * y2 * y2 + d2 * pr * y3 * y1 - d2 * pr * y2 * y3 + x2 * x1
               - d2 * x2 * x1 * pr - x2 * x2 + d2 * x2 * x2 * pr - x3 * x1 + d2 * x3 * x1 * pr
               + x2 * x3 - d2 * x2 * x3 * pr) * cf;
  km[0][5] = (d2 * pr * x1 * y2 - d2 * pr * x1 * y3 + d2 * x2 * y3 * pr + y1 * x2
              - d2 * y1 * x2 * pr - y2 * x2 - y1 * x3 + d2 * y1 * x3 * pr + y2 * x3
              - d2 * pr * y2 * x3) * cf;

  km[1][1] = (-d2 * x2 * x2 + d4 * x2 * x3 - d2 * x3 * x3 + d2 * x2 * x2 * pr
              - d4 * x2 * x3 * pr + d2 * x3 * x3 * pr - y2 * y2 + d2 * pr * y2 * y2
              + d2 * y2 * y3 - d4 * pr * y2 * y3 - y3 * y3 + d2 * pr * y3 * y3) * cf;
  km[1][2] = (d2 * x2 * y3 * pr - d2 * y1 * x2 * pr + d2 * y1 * x3 * pr - x1 * y2
              + d2 * pr * x1 * y2 + y2 * x3 - d2 * pr * y2 * x3 + x1 * y3 - d2 * pr * x1 * y3
              - y3 * x3) * cf;
  km[1][3] = -(-d2 * x2 * x1 + d2 * x2 * x3 + d2 * x3 * x1 - d2 * x3 * x3 + d2 * x2 * x1 * pr
               - d2 * x2 * x3 * pr - d2 * x3 * x1 * pr + d2 * x3 * x3 * pr + y2 * y3
               - d2 * pr * y2 * y3 - y2 * y1 + d2 * pr * y2 * y1 - y3 * y3 + d2 * pr * y3 * y3
               + y3 * y1 - d2 * pr * y3 * y1) * cf;
  km[1][4] = (d2 * y1 * x2 * pr - d2 * y1 * x3 * pr + d2 * pr * y2 * x3 + x1 * y2
              - d2 * pr * x1 * y2 - y2 * x2 - x1 * y3 + d2 * pr * x1 * y3 + x2 * y3
              - d2 * x2 * y3 * pr) * cf;
  km[1][5] = (-d2 * x2 * x1 + d2 * x2 * x2 + d2 * x3 * x1 - d2 * x2 * x3 + d2 * x2 * x1 * pr
              - d2 * x2 * x2 * pr - d2 * x3 * x1 * pr + d2 * x2 * x3 * pr - y2 * y1
              + d2 * pr * y2 * y1 + y2 * y2 - d2 * pr * y2 * y2 + y3 * y1 - d2 * pr * y3 * y1
              - y2 * y3 + d2 * pr * y2 * y3) * cf;

  km[2][2] = (-d2 * y3 * y3 + d4 * y3 * y1 - d2 * y1 * y1 + d2 * pr * y3 * y3
              - d4 * pr * y3 * y1 + d2 * pr * y1 * y1 - x1 * x1 + d2 * x1 * x1 * pr
              + d2 * x3 * x1 - d4 * x3 * x1 * pr - x3 * x3 + d2 * x3 * x3 * pr) * cf;
  km[2][3] = (x1 - x3) * (-y3 + y1) * cf;
  km[2][4] = -(d2 * y3 * y1 - d2 * y2 * y3 - d2 * y1 * y1 + d2 * y2 * y1 - d2 * pr * y3 * y1
               + d2 * pr * y2 * y3 + d2 * pr * y1 * y1 - d2 * pr * y2 * y1 - x1 * x1
               + d2 * x1 * x1 * pr + x2 * x1 - d2 * x2 * x1 * pr + x3 * x1 - d2 * x3 * x1 * pr
               - x2 * x3 + d2 * x2 * x3 * pr) * cf;
  km[2][5] = -(-d2 * pr * x1 * y3 + d2 * x2 * y3 * pr - d2 * y1 * x2 * pr + y1 * x1
               - x1 * y2 + d2 * pr * x1 * y2 - y1 * x3 + d2 * y1 * x3 * pr + y2 * x3
               - d2 * pr * y2 * x3) * cf;

  km[3][3] = (-d2 * x1 * x1 + d4 * x3 * x1 - d2 * x3 * x3 + d2 * x1 * x1 * pr
              - d4 * x3 * x1 * pr + d2 * x3 * x3 * pr - y3 * y3 + d2 * pr * y3 * y3
              + d2 * y3 * y1 - d4 * pr * y3 * y1 - y1 * y1 + d2 * pr * y1 * y1) * cf;
  km[3][4] = (d2 * pr * x1 * y2 + d2 * y1 * x3 * pr - d2 * pr * y2 * x3 + x1 * y3
              - d2 * pr * x1 * y3 - x2 * y3 + d2 * x2 * y3 * pr - y1 * x1 + y1 * x2
              - d2 * y1 * x2 * pr) * cf;
  km[3][5] = -(-d2 * x1 * x1 + d2 * x2 * x1 + d2 * x3 * x1 - d2 * x2 * x3 + d2 * x1 * x1 * pr
               - d2 * x2 * x1 * pr - d2 * x3 * x1 * pr + d2 * x2 * x3 * pr + y3 * y1
               - d2 * pr * y3 * y1 - y2 * y3 + d2 * pr * y2 * y3 - y1 * y1 + d2 * pr * y1 * y1
               + y2 * y1 - d2 * pr * y2 * y1) * cf;

  km[4][4] = (-d2 * y1 * y1 + d4 * y2 * y1 - d2 * y2 * y2 + d2 * pr * y1 * y1
              - d4 * pr * y2 * y1 + d2 * pr * y2 * y2 - x1 * x1 + d2 * x1 * x1 * pr
              + d2 * x2 * x1 - d4 * x2 * x1 * pr - x2 * x2 + d2 * x2 * x2 * pr) * cf;
  km[4][5] = (x1 - x2) * (y1 - y2) * cf;

  km[5][5] = (-d2 * x1 * x1 + d4 * x2 * x1 - d2 * x2 * x2 + d2 * x1 * x1 * pr
              - d4 * x2 * x1 * pr + d2 * x2 * x2 * pr - y1 * y1 + d2 * pr * y1 * y1
              + d2 * y2 * y1 - d4 * pr * y2 * y1 - y2 * y2 + d2 * pr * y2 * y2) * cf;

  km[1][0] = km[0][1]; km[2][0] = km[0][2]; km[2][1] = km[1][2];
  km[3][0] = km[0][3]; km[3][1] = km[1][3]; km[3][2] = km[2][3];
  km[4][0] = km[0][4]; km[4][1] = km[1][4]; km[4][2] = km[2][4]; km[4][3] = km[3][4];
  km[5][0] = km[0][5]; km[5][1] = km[1][5]; km[5][2] = km[2][5];
  km[5][3] = km[3][5]; km[5][4] = km[4][5];
}

} // namespace fem
