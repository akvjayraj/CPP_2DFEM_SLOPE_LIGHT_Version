#include "geom_rect.hpp"
#include <cmath>
#include <stdexcept>

namespace geom {

static const double pt5 = 0.5;
static const double two = 2.0;
static const double d3 = 3.0;

void geom_rect(const std::string& element, int iel,
               const std::vector<double>& x_coords,
               const std::vector<double>& y_coords,
               std::vector<std::vector<double>>& coord,
               std::vector<int>& num, char dir) {
  const int nxe = static_cast<int>(x_coords.size()) - 1;
  const int nod = static_cast<int>(num.size());

  if (element == "triangle") {
    const int nye = (static_cast<int>(y_coords.size()) - 1) * 2;
    int ip, iq, jel;
    if (dir == 'x' || dir == 'r') {
      jel = 2 * nxe * ((iel - 1) / (2 * nxe));
      ip = (iel - jel) / 2;
      if (ip < 1) ip = 1; // (iel-jel)/2 can be 0 for iel==1; x_coords uses 1-based strip index
      iq = 2 * ((iel - 1) / (2 * nxe) + 1) - 1 + ((iel / 2) * 2) / iel;
    } else {
      jel = (iel - 1) / nye;
      ip = jel + 1;
      iq = iel - nye * jel;
    }

    if (nod == 3) {
      if (iq % 2 != 0) {
        if (dir == 'x' || dir == 'r') {
          num[0] = (nxe + 1) * (iq - 1) / 2 + ip;
          num[1] = num[0] + 1;
          num[2] = (nxe + 1) * (iq + 1) / 2 + ip;
        } else {
          num[0] = (ip - 1) * (nye + 2) / 2 + (iq + 1) / 2;
          num[1] = num[0] + (nye + 2) / 2;
          num[2] = num[0] + 1;
        }
        coord[0][0] = x_coords[ip - 1];
        coord[1][0] = y_coords[(iq + 1) / 2 - 1];
        coord[0][1] = x_coords[ip];
        coord[1][1] = y_coords[(iq + 1) / 2 - 1];
        coord[0][2] = x_coords[ip - 1];
        coord[1][2] = y_coords[(iq + 3) / 2 - 1];
      } else {
        if (dir == 'x' || dir == 'r') {
          num[0] = (nxe + 1) * iq / 2 + ip + 1;
          num[1] = num[0] - 1;
          num[2] = (nxe + 1) * (iq - 2) / 2 + ip + 1;
        } else {
          num[0] = ip * (nye + 2) / 2 + (iq + 2) / 2;
          num[1] = (ip - 1) * (nye + 2) / 2 + (iq + 1) / 2 + 1;
          num[2] = num[0] - 1;
        }
        coord[0][0] = x_coords[ip];
        coord[1][0] = y_coords[(iq + 2) / 2 - 1];
        coord[0][1] = x_coords[ip - 1];
        coord[1][1] = y_coords[(iq + 2) / 2 - 1];
        coord[0][2] = x_coords[ip];
        coord[1][2] = y_coords[iq / 2 - 1];
      }
    } else if (nod == 6) {
      if (iq % 2 != 0) {
        if (dir == 'x' || dir == 'r') {
          num[0] = (iq - 1) * (2 * nxe + 1) + 2 * ip - 1;
          num[1] = num[0] + 1;
          num[2] = num[0] + 2;
          num[3] = (iq - 1) * (2 * nxe + 1) + 2 * nxe + 2 * ip + 1;
          num[4] = (iq + 1) * (2 * nxe + 1) + 2 * ip - 1;
          num[5] = num[3] - 1;
        } else {
          num[0] = 2 * (nye + 1) * (ip - 1) + iq;
          num[1] = 2 * (nye + 1) * (ip - 1) + nye + 1 + iq;
          num[2] = 2 * (nye + 1) * ip + iq;
          num[3] = num[1] + 1;
          num[4] = num[0] + 2;
          num[5] = num[0] + 1;
        }
        coord[0][0] = x_coords[ip - 1];
        coord[1][0] = y_coords[(iq + 1) / 2 - 1];
        coord[0][2] = x_coords[ip];
        coord[1][2] = y_coords[(iq + 1) / 2 - 1];
        coord[0][4] = x_coords[ip - 1];
        coord[1][4] = y_coords[(iq + 3) / 2 - 1];
      } else {
        if (dir == 'x' || dir == 'r') {
          num[0] = iq * (2 * nxe + 1) + 2 * ip + 1;
          num[1] = num[0] - 1;
          num[2] = num[0] - 2;
          num[3] = (iq - 2) * (2 * nxe + 1) + 2 * nxe + 2 * ip + 1;
          num[4] = (iq - 2) * (2 * nxe + 1) + 2 * ip + 1;
          num[5] = num[3] + 1;
        } else {
          num[0] = 2 * (nye + 1) * ip + iq + 1;
          num[1] = 2 * (nye + 1) * (ip - 1) + nye + iq + 2;
          num[2] = 2 * (nye + 1) * (ip - 1) + iq + 1;
          num[3] = num[1] - 1;
          num[4] = num[0] - 2;
          num[5] = num[0] - 1;
        }
        coord[0][0] = x_coords[ip];
        coord[1][0] = y_coords[(iq + 2) / 2 - 1];
        coord[0][2] = x_coords[ip - 1];
        coord[1][2] = y_coords[(iq + 2) / 2 - 1];
        coord[0][4] = x_coords[ip];
        coord[1][4] = y_coords[iq / 2 - 1];
      }
      coord[0][1] = pt5 * (coord[0][0] + coord[0][2]);
      coord[1][1] = pt5 * (coord[1][0] + coord[1][2]);
      coord[0][3] = pt5 * (coord[0][2] + coord[0][4]);
      coord[1][3] = pt5 * (coord[1][2] + coord[1][4]);
      coord[0][5] = pt5 * (coord[0][4] + coord[0][0]);
      coord[1][5] = pt5 * (coord[1][4] + coord[1][0]);
    } else if (nod == 10 || nod == 15) {
      throw std::runtime_error("geom_rect: 10 and 15-node triangles not yet implemented");
    } else {
      throw std::runtime_error("Wrong number of nodes for triangular element");
    }
  } else {
    const int nye = static_cast<int>(y_coords.size()) - 1;
    int ip, iq;
    if (dir == 'x' || dir == 'r') {
      iq = (iel - 1) / nxe + 1;
      ip = iel - (iq - 1) * nxe;
    } else {
      ip = (iel - 1) / nye + 1;
      iq = iel - (ip - 1) * nye;
    }

    if (nod == 4) {
      if (dir == 'x' || dir == 'r') {
        num[0] = iq * (nxe + 1) + ip;
        num[1] = (iq - 1) * (nxe + 1) + ip;
        num[2] = num[1] + 1;
        num[3] = num[0] + 1;
      } else {
        num[0] = (ip - 1) * (nye + 1) + iq + 1;
        num[1] = num[0] - 1;
        num[2] = ip * (nye + 1) + iq;
        num[3] = num[2] + 1;
      }
      coord[0][0] = coord[0][1] = x_coords[ip - 1];
      coord[0][2] = coord[0][3] = x_coords[ip];
      coord[1][0] = y_coords[iq];
      coord[1][1] = coord[1][2] = y_coords[iq - 1];
      coord[1][3] = coord[1][0];
    } else if (nod == 5) {
      if (dir == 'x' || dir == 'r') {
        num[0] = iq * (2 * nxe + 1) + ip;
        num[1] = (iq - 1) * (2 * nxe + 1) + ip;
        num[2] = num[1] + 1;
        num[3] = num[0] + 1;
        num[4] = iq * (2 * nxe + 1) + ip - nxe;
      } else {
        num[0] = (ip - 1) * (2 * nye + 1) + iq + 1;
        num[1] = num[0] - 1;
        num[2] = ip * (2 * nye + 1) + iq;
        num[3] = num[2] + 1;
        num[4] = ip * (2 * nye + 1) + iq - nye;
      }
      coord[0][0] = coord[0][1] = x_coords[ip - 1];
      coord[0][2] = coord[0][3] = x_coords[ip];
      coord[1][0] = y_coords[iq];
      coord[1][1] = coord[1][2] = y_coords[iq - 1];
      coord[1][3] = coord[1][0];
      coord[0][4] = 0.25 * (coord[0][0] + coord[0][1] + coord[0][2] + coord[0][3]);
      coord[1][4] = 0.25 * (coord[1][0] + coord[1][1] + coord[1][2] + coord[1][3]);
    } else if (nod == 8) {
      if (dir == 'x' || dir == 'r') {
        num[0] = iq * (3 * nxe + 2) + 2 * ip - 1;
        num[1] = iq * (3 * nxe + 2) + ip - nxe - 1;
        num[2] = (iq - 1) * (3 * nxe + 2) + 2 * ip - 1;
        num[3] = num[2] + 1;
        num[4] = num[3] + 1;
        num[5] = num[1] + 1;
        num[6] = num[0] + 2;
        num[7] = num[0] + 1;
      } else {
        num[0] = (ip - 1) * (3 * nye + 2) + 2 * iq + 1;
        num[1] = num[0] - 1;
        num[2] = num[0] - 2;
        num[3] = (ip - 1) * (3 * nye + 2) + 2 * nye + iq + 1;
        num[4] = ip * (3 * nye + 2) + 2 * iq - 1;
        num[5] = num[4] + 1;
        num[6] = num[4] + 2;
        num[7] = num[3] + 1;
      }
      coord[0][0] = coord[0][1] = coord[0][2] = x_coords[ip - 1];
      coord[0][4] = coord[0][5] = coord[0][6] = x_coords[ip];
      coord[0][3] = pt5 * (coord[0][2] + coord[0][4]);
      coord[0][7] = pt5 * (coord[0][6] + coord[0][0]);
      coord[1][0] = y_coords[iq];
      coord[1][6] = coord[1][7] = y_coords[iq];
      coord[1][2] = coord[1][3] = coord[1][4] = y_coords[iq - 1];
      coord[1][1] = pt5 * (coord[1][0] + coord[1][2]);
      coord[1][5] = pt5 * (coord[1][4] + coord[1][6]);
    } else if (nod == 9) {
      if (dir == 'x' || dir == 'r') {
        num[0] = iq * (4 * nxe + 2) + 2 * ip - 1;
        num[1] = iq * (4 * nxe + 2) + 2 * ip - nxe - 4;
        num[2] = (iq - 1) * (4 * nxe + 2) + 2 * ip - 1;
        num[3] = num[2] + 1;
        num[4] = num[3] + 1;
        num[5] = num[1] + 2;
        num[6] = num[0] + 2;
        num[7] = num[0] + 1;
        num[8] = num[1] + 1;
      } else {
        num[0] = (ip - 1) * 2 * (2 * nye + 1) + 2 * iq + 1;
        num[1] = num[0] - 1;
        num[2] = num[0] - 2;
        num[3] = (ip - 1) * 2 * (2 * nye + 1) + 2 * nye + 2 * iq;
        num[4] = ip * 2 * (2 * nye + 1) + 2 * iq - 1;
        num[5] = num[4] + 1;
        num[6] = num[4] + 2;
        num[7] = num[3] + 2;
        num[8] = num[3] + 1;
      }
      coord[0][0] = coord[0][1] = coord[0][2] = x_coords[ip - 1];
      coord[0][4] = coord[0][5] = coord[0][6] = x_coords[ip];
      coord[0][3] = pt5 * (coord[0][2] + coord[0][4]);
      coord[0][7] = pt5 * (coord[0][6] + coord[0][0]);
      coord[1][0] = y_coords[iq];
      coord[1][6] = coord[1][7] = y_coords[iq];
      coord[1][2] = coord[1][3] = coord[1][4] = y_coords[iq - 1];
      coord[1][1] = pt5 * (coord[1][0] + coord[1][2]);
      coord[1][5] = pt5 * (coord[1][4] + coord[1][6]);
      coord[0][8] = pt5 * (coord[0][3] + coord[0][7]);
      coord[1][8] = pt5 * (coord[1][3] + coord[1][7]);
    } else {
      throw std::runtime_error("Wrong number of nodes for quadrilateral element");
    }
  }
}

} // namespace geom
