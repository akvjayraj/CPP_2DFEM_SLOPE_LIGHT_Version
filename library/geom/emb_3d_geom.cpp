#include "emb_3d_geom.hpp"

namespace geom {

void emb_3d_geom(int iel, int nx1, int nx2, int ny1, int ny2, int nze,
                 double w1, double s1, double w2, double h1, double h2, double d1,
                 std::vector<std::vector<double>>& coord, std::vector<int>& num) {
  const double pt5 = 0.5;
  const double zero = 0.0;

  int nxe = nx1 + nx2;
  int nye = ny1 + ny2;
  int ns = nx1 * nye + nx2 * ny2;
  int nt = nx1 * ny1;
  int nc = (3 * nye + 2) * nx1 + 2 * ny1;
  int nd = (nye + 1) * nx1 + ny1;
  int nn_mid = (nye + 1) * (nx1 + 1) + (ny2 + 1) * nx2;
  int nn_main = (3 * nye + 2) * nx1 + 2 * nye + 1 + (3 * ny2 + 2) * nx2;

  double facx = s1 / ny1;
  double facy = h1 / ny1;
  double facz = d1 / nze;
  double facb = (w1 + s1) / nx1;
  double facs = zero;
  if (ny2 != 0) facs = h2 / ny2;
  double frh = zero;
  if (nx2 != 0) frh = w2 / nx2;

  int is = (iel - 1) / ns + 1;
  int ip, iq;
  if (iel - (is - 1) * ns <= nt) {
    iq = (iel - (is - 1) * ns - 1) / nx1 + 1;
    ip = iel - (is - 1) * ns - (iq - 1) * nx1;
  } else {
    iq = (iel - (is - 1) * ns - nt - 1) / nxe + ny1 + 1;
    ip = iel - (is - 1) * ns - nt - (iq - ny1 - 1) * nxe;
  }

  if (ip <= nx1) {
    num[0] = (ip - 1) * (3 * nye + 2) + 2 * iq + 1 + (is - 1) * (nn_mid + nn_main);
    num[1] = num[0] - 1;
    num[2] = num[0] - 2;
    num[3] = (ip - 1) * (3 * nye + 2) + 2 * nye + iq + 1 + (is - 1) * (nn_mid + nn_main);
    num[4] = ip * (3 * nye + 2) + 2 * iq - 1 + (is - 1) * (nn_mid + nn_main);
    num[5] = num[4] + 1;
    num[6] = num[4] + 2;
    num[7] = num[3] + 1;
    num[8] = (nn_main + (ip - 1) * (nye + 1) + iq) * is + (is - 1) * (nn_mid - (ip - 1) * (nye + 1) - iq - 1);
    num[9] = num[8] - 1;
    num[10] = 1 + (nn_main + ip * (nye + 1) + iq - 1) * is + (is - 1) * (nn_mid - ip * (nye + 1) - iq + 1);
    num[11] = num[10] + 1;
    num[12] = (ip - 1) * (3 * nye + 2) + 2 * iq + 1 + is * (nn_mid + nn_main);
    num[13] = num[12] - 1;
    num[14] = num[12] - 2;
    num[15] = (ip - 1) * (3 * nye + 2) + 2 * nye + iq + 1 + is * (nn_mid + nn_main);
    num[16] = ip * (3 * nye + 2) + 2 * iq - 1 + is * (nn_mid + nn_main);
    num[17] = num[16] + 1;
    num[18] = num[16] + 2;
    num[19] = num[15] + 1;

    if (iq <= ny1) {
      coord[0][0] = (ip - 1) * (w1 + iq * facx) / nx1;
      coord[0][2] = (ip - 1) * (w1 + (iq - 1) * facx) / nx1;
      coord[0][4] = ip * (w1 + (iq - 1) * facx) / nx1;
      coord[0][6] = ip * (w1 + iq * facx) / nx1;
      coord[1][0] = -iq * facy;
      coord[1][2] = -(iq - 1) * facy;
      coord[1][4] = -(iq - 1) * facy;
      coord[1][6] = -iq * facy;
    } else {
      coord[0][0] = (ip - 1) * facb;
      coord[0][2] = (ip - 1) * facb;
      coord[0][4] = ip * facb;
      coord[0][6] = ip * facb;
      coord[1][0] = -h1 - (iq - ny1) * facs;
      coord[1][2] = -h1 - (iq - ny1 - 1) * facs;
      coord[1][4] = -h1 - (iq - ny1 - 1) * facs;
      coord[1][6] = -h1 - (iq - ny1) * facs;
    }
  } else {
    num[0] = (nn_mid + nn_main) * (is - 1) + nc + (3 * ny2 + 2) * (ip - nx1 - 1) + (iq - ny1) * 2 + 1;
    num[1] = num[0] - 1;
    num[2] = num[0] - 2;
    num[3] = (nn_mid + nn_main) * (is - 1) + nc + (3 * ny2 + 2) * (ip - nx1 - 1) + 2 * ny2 + 1 + iq - ny1;
    num[4] = (nn_mid + nn_main) * (is - 1) + nc + (3 * ny2 + 2) * (ip - nx1) + (iq - ny1) * 2 - 1;
    num[5] = num[4] + 1;
    num[6] = num[4] + 2;
    num[7] = num[3] + 1;
    num[8] = (nn_mid + nn_main) * (is - 1) + nn_main + nd + (ny2 + 1) * (ip - nx1 - 1) + iq - ny1 + 1;
    num[9] = num[8] - 1;
    num[10] = (nn_mid + nn_main) * (is - 1) + nn_main + nd + (ny2 + 1) * (ip - nx1) + iq - ny1;
    num[11] = num[10] + 1;
    num[12] = (nn_mid + nn_main) * is + nc + (3 * ny2 + 2) * (ip - nx1 - 1) + (iq - ny1) * 2 + 1;
    num[13] = num[12] - 1;
    num[14] = num[12] - 2;
    num[15] = (nn_mid + nn_main) * is + nc + (3 * ny2 + 2) * (ip - nx1 - 1) + 2 * ny2 + 1 + iq - ny1;
    num[16] = (nn_mid + nn_main) * is + nc + (3 * ny2 + 2) * (ip - nx1) + (iq - ny1) * 2 - 1;
    num[17] = num[16] + 1;
    num[18] = num[16] + 2;
    num[19] = num[15] + 1;

    coord[0][0] = w1 + s1 + (ip - nx1 - 1) * frh;
    coord[0][2] = coord[0][0];
    coord[0][4] = w1 + s1 + (ip - nx1) * frh;
    coord[0][6] = coord[0][4];
    coord[1][0] = -h1 - (iq - ny1) * facs;
    coord[1][2] = -h1 - (iq - ny1 - 1) * facs;
    coord[1][4] = -h1 - (iq - ny1 - 1) * facs;
    coord[1][6] = -h1 - (iq - ny1) * facs;
  }

  coord[0][8] = coord[0][0];
  coord[0][9] = coord[0][2];
  coord[0][10] = coord[0][4];
  coord[0][11] = coord[0][6];
  coord[0][12] = coord[0][0];
  coord[0][14] = coord[0][2];
  coord[0][16] = coord[0][4];
  coord[0][18] = coord[0][6];
  coord[1][8] = coord[1][0];
  coord[1][9] = coord[1][2];
  coord[1][10] = coord[1][4];
  coord[1][11] = coord[1][6];
  coord[1][12] = coord[1][0];
  coord[1][14] = coord[1][2];
  coord[1][16] = coord[1][4];
  coord[1][18] = coord[1][6];

  for (int k = 0; k < 8; ++k)
    coord[2][k] = -(is - 1) * facz;
  for (int k = 8; k < 12; ++k)
    coord[2][k] = -(is * facz - facz * pt5);
  for (int k = 12; k < 20; ++k)
    coord[2][k] = -is * facz;

  coord[0][1] = pt5 * (coord[0][0] + coord[0][2]);
  coord[0][3] = pt5 * (coord[0][2] + coord[0][4]);
  coord[0][5] = pt5 * (coord[0][4] + coord[0][6]);
  coord[0][7] = pt5 * (coord[0][6] + coord[0][0]);
  coord[0][13] = pt5 * (coord[0][12] + coord[0][14]);
  coord[0][15] = pt5 * (coord[0][14] + coord[0][16]);
  coord[0][17] = pt5 * (coord[0][16] + coord[0][18]);
  coord[0][19] = pt5 * (coord[0][18] + coord[0][12]);
  coord[1][1] = pt5 * (coord[1][0] + coord[1][2]);
  coord[1][3] = pt5 * (coord[1][2] + coord[1][4]);
  coord[1][5] = pt5 * (coord[1][4] + coord[1][6]);
  coord[1][7] = pt5 * (coord[1][6] + coord[1][0]);
  coord[1][13] = pt5 * (coord[1][12] + coord[1][14]);
  coord[1][15] = pt5 * (coord[1][14] + coord[1][16]);
  coord[1][17] = pt5 * (coord[1][16] + coord[1][18]);
  coord[1][19] = pt5 * (coord[1][18] + coord[1][12]);
}

} // namespace geom
