#include "shape_der.hpp"
#include <stdexcept>

namespace fem {

void shape_der(std::vector<std::vector<double>>& der,
               const std::vector<std::vector<double>>& points, int i) {
  int ndim = static_cast<int>(der.size());
  int nod = static_cast<int>(der[0].size());
  const double zero = 0.0, one = 1.0, pt25 = 0.25, pt5 = 0.5;

  if (ndim == 2) {
    double xi = points[i - 1][0], eta = points[i - 1][1];
    double c1 = xi, c2 = eta, c3 = one - c1 - c2;
    double xim = pt25 * (one - xi), xip = pt25 * (one + xi);
    double etam = pt25 * (one - eta), etap = pt25 * (one + eta);

    if (nod == 3) {
      der[0][0] = one;
      der[0][1] = -one;
      der[0][2] = zero;
      der[1][0] = zero;
      der[1][1] = -one;
      der[1][2] = one;
    } else if (nod == 6) {
      // Quadratic triangle derivatives for L1=1−ξ−η, L2=ξ, L3=η (matches shape_fun nod==6).
      const double L1 = c3, L2 = c1, L3 = c2;
      der[0][0] = -(4.0 * L1 - one);
      der[1][0] = -(4.0 * L1 - one);
      der[0][1] = 4.0 * L2 - one;
      der[1][1] = zero;
      der[0][2] = zero;
      der[1][2] = 4.0 * L3 - one;
      der[0][3] = 4.0 * (L1 - L2);
      der[1][3] = -4.0 * L2;
      der[0][4] = 4.0 * L3;
      der[1][4] = 4.0 * L2;
      der[0][5] = -4.0 * L3;
      der[1][5] = 4.0 * (L1 - L3);
    } else if (nod == 4) {
      der[0][0] = -etam;
      der[0][1] = -etap;
      der[0][2] = etap;
      der[0][3] = etam;
      der[1][0] = -xim;
      der[1][1] = xim;
      der[1][2] = xip;
      der[1][3] = -xip;
    } else if (nod == 8) {
      double x = xi, e = eta;
      // Book order BL,BR,TR,TL,BM,RM,TM,LM — permute to emb_2d_geom node order.
      double ds0dxi = pt25 * (-(1.0 - e) * (-1.0 - x - e) - (1.0 - x) * (1.0 - e));
      double ds0det = pt25 * (-(1.0 - x) * (-1.0 - x - e) - (1.0 - x) * (1.0 - e));
      double ds1dxi = pt25 * ((1.0 - e) * (x - e - 1.0) + (1.0 + x) * (1.0 - e));
      double ds1det = pt25 * (-(1.0 + x) * (x - e - 1.0) - (1.0 + x) * (1.0 - e));
      double ds2dxi = pt25 * ((1.0 + e) * (x + e - 1.0) + (1.0 + x) * (1.0 + e));
      double ds2det = pt25 * ((1.0 + x) * (x + e - 1.0) + (1.0 + x) * (1.0 + e));
      double ds3dxi = pt25 * (-(1.0 + e) * (-x + e - 1.0) - (1.0 - x) * (1.0 + e));
      double ds3det = pt25 * ((1.0 - x) * (-x + e - 1.0) + (1.0 - x) * (1.0 + e));
      double ds4dxi = pt5 * (-2.0 * x) * (1.0 - e);
      double ds4det = pt5 * (1.0 - x * x) * (-1.0);
      double ds5dxi = pt5 * (1.0 - e * e);
      double ds5det = pt5 * (1.0 + x) * (-2.0 * e);
      double ds6dxi = pt5 * (-2.0 * x) * (1.0 + e);
      double ds6det = pt5 * (1.0 - x * x);
      double ds7dxi = pt5 * (-1.0) * (1.0 - e * e);
      double ds7det = pt5 * (1.0 - x) * (-2.0 * e);
      const int P[8] = {1, 4, 0, 7, 3, 6, 2, 5};
      double d0[8] = {ds0dxi, ds1dxi, ds2dxi, ds3dxi, ds4dxi, ds5dxi, ds6dxi, ds7dxi};
      double d1[8] = {ds0det, ds1det, ds2det, ds3det, ds4det, ds5det, ds6det, ds7det};
      for (int j = 0; j < 8; ++j) {
        der[0][j] = d0[P[j]];
        der[1][j] = d1[P[j]];
      }
    } else {
      throw std::runtime_error("shape_der: unsupported nod for 2D");
    }
  } else if (ndim == 3) {
    const double pt125 = 0.125;
    double xi = points[i - 1][0], eta = points[i - 1][1], zeta = points[i - 1][2];
    double xim = one - xi, xip = one + xi;
    double etam = one - eta, etap = eta + one;
    double zetam = one - zeta, zetap = zeta + one;
    if (nod == 8) {
      der[0][0] = -pt125 * etam * zetam;
      der[0][1] = -pt125 * etam * zetap;
      der[0][2] = pt125 * etam * zetap;
      der[0][3] = pt125 * etam * zetam;
      der[0][4] = -pt125 * etap * zetam;
      der[0][5] = -pt125 * etap * zetap;
      der[0][6] = pt125 * etap * zetap;
      der[0][7] = pt125 * etap * zetam;
      der[1][0] = -pt125 * xim * zetam;
      der[1][1] = -pt125 * xim * zetap;
      der[1][2] = -pt125 * xip * zetap;
      der[1][3] = -pt125 * xip * zetam;
      der[1][4] = pt125 * xim * zetam;
      der[1][5] = pt125 * xim * zetap;
      der[1][6] = pt125 * xip * zetap;
      der[1][7] = pt125 * xip * zetam;
      der[2][0] = -pt125 * xim * etam;
      der[2][1] = pt125 * xim * etam;
      der[2][2] = pt125 * xip * etam;
      der[2][3] = -pt125 * xip * etam;
      der[2][4] = -pt125 * xim * etap;
      der[2][5] = pt125 * xim * etap;
      der[2][6] = pt125 * xip * etap;
      der[2][7] = -pt125 * xip * etap;
    } else {
      throw std::runtime_error("shape_der: unsupported nod for 3D");
    }
  } else {
    throw std::runtime_error("shape_der: unsupported ndim");
  }
}

} // namespace fem
