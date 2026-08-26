#include "hexahedron_xz.hpp"

namespace geom {

void hexahedron_xz(int iel,
                  const std::vector<double>& x_coords,
                  const std::vector<double>& y_coords,
                  const std::vector<double>& z_coords,
                  std::vector<std::vector<double>>& coord,
                  std::vector<int>& num) {
  const double pt5 = 0.5;
  const int nxe = static_cast<int>(x_coords.size()) - 1;
  const int nze = static_cast<int>(z_coords.size()) - 1;
  const int nod = static_cast<int>(num.size());

  int iq = (iel - 1) / (nxe * nze) + 1;
  int iplane = iel - (iq - 1) * nxe * nze;
  int is = (iplane - 1) / nxe + 1;
  int ip = iplane - (is - 1) * nxe;

  if (nod == 8) {
    int fac1 = (nxe + 1) * (nze + 1) * (iq - 1);
    num[0] = fac1 + is * (nxe + 1) + ip;
    num[1] = fac1 + (is - 1) * (nxe + 1) + ip;
    num[2] = num[1] + 1;
    num[3] = num[0] + 1;
    num[4] = (nxe + 1) * (nze + 1) * iq + is * (nxe + 1) + ip;
    num[5] = (nxe + 1) * (nze + 1) * iq + (is - 1) * (nxe + 1) + ip;
    num[6] = num[5] + 1;
    num[7] = num[4] + 1;

    coord[0][0] = coord[0][1] = x_coords[ip - 1];
    coord[0][4] = coord[0][5] = x_coords[ip - 1];
    coord[0][2] = coord[0][3] = x_coords[ip];
    coord[0][6] = coord[0][7] = x_coords[ip];

    coord[1][0] = coord[1][1] = coord[1][2] = coord[1][3] = y_coords[iq - 1];
    coord[1][4] = coord[1][5] = coord[1][6] = coord[1][7] = y_coords[iq];

    coord[2][1] = coord[2][2] = z_coords[is - 1];
    coord[2][5] = coord[2][6] = z_coords[is - 1];
    coord[2][0] = coord[2][3] = z_coords[is];
    coord[2][4] = coord[2][7] = z_coords[is];
  } else if (nod == 14) {
    int fac1 = (2 * nxe + 1) * (2 * nze + 1) * (iq - 1);
    int fac2 = (2 * nxe + 1) * (2 * nze + 1) * iq;
    num[0] = fac1 + is * (2 * nxe + 1) + ip;
    num[1] = num[0] - (2 * nxe + 1);
    num[2] = num[1] + 1;
    num[3] = num[0] + 1;
    num[4] = num[1] + nxe + 1;
    num[5] = fac1 + (nxe + 1) * (nze + 1) + nxe * nze + (is - 1) * (2 * nxe + 1) + nxe + ip;
    num[6] = num[5] - nxe;
    num[7] = num[5] + 1;
    num[8] = num[7] + nxe;
    num[9] = fac2 + is * (2 * nxe + 1) + ip;
    num[10] = num[9] - (2 * nxe + 1);
    num[11] = num[10] + 1;
    num[12] = num[9] + 1;
    num[13] = num[10] + nxe + 1;

    coord[0][0] = coord[0][1] = x_coords[ip - 1];
    coord[0][5] = x_coords[ip - 1];
    coord[0][9] = coord[0][10] = x_coords[ip - 1];
    coord[0][4] = coord[0][6] = coord[0][8] = pt5 * (x_coords[ip - 1] + x_coords[ip]);
    coord[0][13] = pt5 * (x_coords[ip - 1] + x_coords[ip]);
    coord[0][2] = coord[0][3] = x_coords[ip];
    coord[0][7] = x_coords[ip];
    coord[0][11] = coord[0][12] = x_coords[ip];

    coord[1][0] = coord[1][1] = coord[1][2] = coord[1][3] = coord[1][4] = y_coords[iq - 1];
    coord[1][5] = coord[1][6] = coord[1][7] = coord[1][8] = pt5 * (y_coords[iq - 1] + y_coords[iq]);
    coord[1][9] = coord[1][10] = coord[1][11] = coord[1][12] = coord[1][13] = y_coords[iq];

    coord[2][1] = coord[2][2] = z_coords[is - 1];
    coord[2][6] = z_coords[is - 1];
    coord[2][10] = coord[2][11] = z_coords[is - 1];
    coord[2][4] = coord[2][5] = pt5 * (z_coords[is - 1] + z_coords[is]);
    coord[2][7] = coord[2][13] = pt5 * (z_coords[is - 1] + z_coords[is]);
    coord[2][0] = coord[2][3] = z_coords[is];
    coord[2][8] = z_coords[is];
    coord[2][9] = coord[2][12] = z_coords[is];
  } else if (nod == 20) {
    int fac1 = ((2 * nxe + 1) * (nze + 1) + (2 * nze + 1) * (nxe + 1)) * (iq - 1);
    int fac2 = ((2 * nxe + 1) * (nze + 1) + (2 * nze + 1) * (nxe + 1)) * iq;
    num[0] = fac1 + (3 * nxe + 2) * is + 2 * ip - 1;
    num[1] = fac1 + (3 * nxe + 2) * is - nxe + ip - 1;
    num[2] = num[0] - 3 * nxe - 2;
    num[3] = num[2] + 1;
    num[4] = num[3] + 1;
    num[5] = num[1] + 1;
    num[6] = num[0] + 2;
    num[7] = num[0] + 1;
    num[8] = fac2 - (nxe + 1) * (nze + 1) + (nxe + 1) * is + ip;
    num[9] = num[8] - nxe - 1;
    num[10] = num[9] + 1;
    num[11] = num[8] + 1;
    num[12] = fac2 + (3 * nxe + 2) * is + 2 * ip - 1;
    num[13] = fac2 + (3 * nxe + 2) * is - nxe + ip - 1;
    num[14] = num[12] - 3 * nxe - 2;
    num[15] = num[14] + 1;
    num[16] = num[15] + 1;
    num[17] = num[13] + 1;
    num[18] = num[12] + 2;
    num[19] = num[12] + 1;

    coord[0][0] = coord[0][1] = coord[0][2] = x_coords[ip - 1];
    coord[0][8] = coord[0][9] = x_coords[ip - 1];
    coord[0][12] = coord[0][13] = coord[0][14] = x_coords[ip - 1];
    coord[0][4] = coord[0][5] = coord[0][6] = x_coords[ip];
    coord[0][10] = coord[0][11] = x_coords[ip];
    coord[0][16] = coord[0][17] = coord[0][18] = x_coords[ip];
    coord[0][3] = coord[0][7] = pt5 * (x_coords[ip - 1] + x_coords[ip]);
    coord[0][15] = coord[0][19] = pt5 * (x_coords[ip - 1] + x_coords[ip]);

    coord[1][0] = coord[1][1] = coord[1][2] = coord[1][3] = coord[1][4] = coord[1][5] = coord[1][6] = coord[1][7] = y_coords[iq - 1];
    coord[1][12] = coord[1][13] = coord[1][14] = coord[1][15] = coord[1][16] = coord[1][17] = coord[1][18] = coord[1][19] = y_coords[iq];
    coord[1][8] = coord[1][9] = coord[1][10] = coord[1][11] = pt5 * (y_coords[iq - 1] + y_coords[iq]);

    coord[2][0] = z_coords[is];
    coord[2][6] = coord[2][7] = coord[2][8] = z_coords[is];
    coord[2][11] = coord[2][12] = z_coords[is];
    coord[2][18] = coord[2][19] = z_coords[is];
    coord[2][2] = coord[2][3] = coord[2][4] = z_coords[is - 1];
    coord[2][9] = coord[2][10] = z_coords[is - 1];
    coord[2][14] = coord[2][15] = coord[2][16] = z_coords[is - 1];
    coord[2][1] = coord[2][5] = pt5 * (z_coords[is - 1] + z_coords[is]);
    coord[2][13] = coord[2][17] = pt5 * (z_coords[is - 1] + z_coords[is]);
  }
}

} // namespace geom
