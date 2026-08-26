#include "emb_2d_geom.hpp"

namespace geom {

void emb_2d_geom(int iel, int nx1, int nx2, int ny1, int ny2,
                 double w1, double s1, double w2, double h1, double h2,
                 std::vector<std::vector<double>>& coord,
                 std::vector<int>& num) {
  const double pt5 = 0.5;
  const double one = 1.0;
  const double zero = 0.0;

  int nxe = nx1 + nx2;
  int nye = ny1 + ny2;
  int nt = nx1 * ny1;
  int nc = (3 * nye + 2) * nx1 + 2 * ny1;

  double facx = s1 / ny1;
  double facy = h1 / ny1;
  double facb = (w1 + s1) / nx1;
  double facs = zero;
  if (ny2 != 0) facs = h2 / ny2;
  double frh = zero;
  if (nx2 != 0) frh = w2 / nx2;

  int ip, iq;
  if (iel <= nt) {
    iq = (iel - 1) / nx1 + 1;
    ip = iel - (iq - 1) * nx1;
  } else {
    iq = (iel - nt - 1) / nxe + ny1 + 1;
    ip = iel - nt - (iq - ny1 - 1) * nxe;
  }

  if (ip <= nx1) {
    num[0] = (ip - 1) * (3 * nye + 2) + 2 * iq + 1;
    num[1] = num[0] - 1;
    num[2] = num[0] - 2;
    num[3] = (ip - 1) * (3 * nye + 2) + 2 * nye + iq + 1;
    num[4] = ip * (3 * nye + 2) + 2 * iq - 1;
    num[5] = num[4] + 1;
    num[6] = num[4] + 2;
    num[7] = num[3] + 1;

    if (iq <= ny1) {
      coord[0][0] = (ip - one) * (w1 + iq * facx) / nx1;
      coord[0][2] = (ip - one) * (w1 + (iq - 1) * facx) / nx1;
      coord[0][4] = ip * (w1 + (iq - 1) * facx) / nx1;
      coord[0][6] = ip * (w1 + iq * facx) / nx1;
      coord[1][0] = -iq * facy;
      coord[1][2] = -(iq - 1) * facy;
      coord[1][4] = -(iq - 1) * facy;
      coord[1][6] = -iq * facy;
    } else {
      coord[0][0] = (ip - one) * facb;
      coord[0][2] = (ip - one) * facb;
      coord[0][4] = ip * facb;
      coord[0][6] = ip * facb;
      coord[1][0] = -h1 - (iq - ny1) * facs;
      coord[1][2] = -h1 - (iq - ny1 - 1) * facs;
      coord[1][4] = -h1 - (iq - ny1 - 1) * facs;
      coord[1][6] = -h1 - (iq - ny1) * facs;
    }
  } else {
    num[0] = nc + (ip - nx1 - 1) * (3 * ny2 + 2) + 2 * (iq - ny1) + 1;
    num[1] = num[0] - 1;
    num[2] = num[0] - 2;
    num[3] = nc + (ip - nx1 - 1) * (3 * ny2 + 2) + 2 * ny2 + iq - ny1 + 1;
    num[4] = nc + (ip - nx1) * (3 * ny2 + 2) + 2 * (iq - ny1) - 1;
    num[5] = num[4] + 1;
    num[6] = num[4] + 2;
    num[7] = num[3] + 1;

    coord[0][0] = w1 + s1 + (ip - nx1 - 1) * frh;
    coord[0][2] = coord[0][0];
    coord[0][4] = w1 + s1 + (ip - nx1) * frh;
    coord[0][6] = coord[0][4];
    coord[1][0] = -h1 - (iq - ny1) * facs;
    coord[1][2] = -h1 - (iq - ny1 - 1) * facs;
    coord[1][4] = -h1 - (iq - ny1 - 1) * facs;
    coord[1][6] = -h1 - (iq - ny1) * facs;
  }

  coord[0][1] = pt5 * (coord[0][0] + coord[0][2]);
  coord[1][1] = pt5 * (coord[1][0] + coord[1][2]);
  coord[0][3] = pt5 * (coord[0][2] + coord[0][4]);
  coord[1][3] = pt5 * (coord[1][2] + coord[1][4]);
  coord[0][5] = pt5 * (coord[0][4] + coord[0][6]);
  coord[1][5] = pt5 * (coord[1][4] + coord[1][6]);
  coord[0][7] = pt5 * (coord[0][6] + coord[0][0]);
  coord[1][7] = pt5 * (coord[1][6] + coord[1][0]);
}

} // namespace geom
