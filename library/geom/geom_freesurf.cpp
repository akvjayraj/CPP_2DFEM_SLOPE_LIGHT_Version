#include "geom_freesurf.hpp"
#include <cmath>

namespace geom {

void geom_freesurf(int iel, int nxe, int fixed_seep, int fixed_down, double down,
                   const std::vector<double>& width, const std::vector<double>& angs,
                   const std::vector<double>& surf,
                   std::vector<std::vector<double>>& coord, std::vector<int>& num) {
  const double pi = std::acos(-1.0);
  const double d180 = 180.0;
  const double small = 0.0001;
  const double pt5 = 0.5;
  const double one = 1.0;

  int iq = (iel - 1) / nxe + 1;
  int ip = iel - (iq - 1) * nxe;

  num[0] = iq * (nxe + 1) + ip;
  num[1] = (iq - 1) * (nxe + 1) + ip;
  num[2] = num[1] + 1;
  num[3] = num[0] + 1;

  double b1, b2;
  if (iq <= fixed_seep + 1) {
    b1 = (surf[ip - 1] - down) / (fixed_seep + 1);
    b2 = (surf[ip] - down) / (fixed_seep + 1);
    coord[1][0] = down + (fixed_seep + 1 - iq) * b1;
    coord[1][1] = down + (fixed_seep + 2 - iq) * b1;
    coord[1][2] = down + (fixed_seep + 2 - iq) * b2;
    coord[1][3] = down + (fixed_seep + 1 - iq) * b2;
  } else {
    b1 = static_cast<double>(fixed_down + fixed_seep - iq) / (fixed_down - 1);
    b2 = static_cast<double>(fixed_down + fixed_seep - iq + 1) / (fixed_down - 1);
    coord[1][0] = down * b1;
    coord[1][1] = down * b2;
    coord[1][2] = coord[1][1];
    coord[1][3] = coord[1][0];
  }

  double fac1, fac2;
  double angr_ip = angs[ip - 1] * pi / d180;
  double angr_ip1 = angs[ip] * pi / d180;

  if (std::abs(angr_ip - pi * pt5) < small)
    fac1 = 0.0;
  else
    fac1 = one / std::tan(angr_ip);

  if (std::abs(angr_ip1 - pi * pt5) < small)
    fac2 = 0.0;
  else
    fac2 = one / std::tan(angr_ip1);

  coord[0][0] = width[ip - 1] + coord[1][0] * fac1;
  coord[0][1] = width[ip - 1] + coord[1][1] * fac1;
  coord[0][2] = width[ip] + coord[1][2] * fac2;
  coord[0][3] = width[ip] + coord[1][3] * fac2;
}

} // namespace geom
