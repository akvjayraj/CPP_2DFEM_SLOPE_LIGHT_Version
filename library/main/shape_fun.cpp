#include "shape_fun.hpp"
#include <stdexcept>

namespace fem {

void shape_fun(std::vector<double>& fun, const std::vector<std::vector<double>>& points, int i) {
  int ndim = static_cast<int>(points[0].size());
  int nod = static_cast<int>(fun.size());
  const double one = 1.0, pt25 = 0.25, pt5 = 0.5, d4 = 4.0;

  if (ndim == 2) {
    double xi = points[i - 1][0], eta = points[i - 1][1];
    double c1 = xi, c2 = eta, c3 = one - c1 - c2;
    double xim = pt25 * (one - xi), xip = pt25 * (one + xi);
    double etam = pt25 * (one - eta), etap = pt25 * (one + eta);

    if (nod == 3) {
      fun[0] = c1;
      fun[1] = c3;
      fun[2] = c2;
    } else if (nod == 6) {
      // Quadratic triangle: corners at L1=1−ξ−η, L2=ξ, L3=η; mids on edges 1–2, 2–3, 3–1.
      const double L1 = c3, L2 = c1, L3 = c2;
      fun[0] = L1 * (2.0 * L1 - one);
      fun[1] = L2 * (2.0 * L2 - one);
      fun[2] = L3 * (2.0 * L3 - one);
      fun[3] = d4 * L1 * L2;
      fun[4] = d4 * L2 * L3;
      fun[5] = d4 * L3 * L1;
    } else if (nod == 4) {
      fun[0] = d4 * xim * etam;
      fun[1] = d4 * xim * etap;
      fun[2] = d4 * xip * etap;
      fun[3] = d4 * xip * etam;
    } else if (nod == 8) {
      // Serendipity quad in book order (BL,BR,TR,TL,BM,RM,TM,LM); permute to emb_2d_geom
      // corners 0,2,4,6 = BR,BL,TL,TR and mids 1,3,5,7 = BM,LM,TM,RM.
      double x = xi, e = eta;
      double s0 = pt25 * (1.0 - x) * (1.0 - e) * (-1.0 - x - e);
      double s1 = pt25 * (1.0 + x) * (1.0 - e) * (x - e - 1.0);
      double s2 = pt25 * (1.0 + x) * (1.0 + e) * (x + e - 1.0);
      double s3 = pt25 * (1.0 - x) * (1.0 + e) * (-x + e - 1.0);
      double s4 = pt5 * (1.0 - x * x) * (1.0 - e);
      double s5 = pt5 * (1.0 + x) * (1.0 - e * e);
      double s6 = pt5 * (1.0 - x * x) * (1.0 + e);
      double s7 = pt5 * (1.0 - x) * (1.0 - e * e);
      const int P[8] = {1, 4, 0, 7, 3, 6, 2, 5};
      double fs[8] = {s0, s1, s2, s3, s4, s5, s6, s7};
      for (int j = 0; j < 8; ++j) fun[j] = fs[P[j]];
    } else {
      throw std::runtime_error("shape_fun: unsupported nod for 2D");
    }
  } else if (ndim == 3) {
    const double pt125 = 0.125;
    double xi = points[i - 1][0], eta = points[i - 1][1], zeta = points[i - 1][2];
    double xim = one - xi, xip = one + xi;
    double etam = one - eta, etap = eta + one;
    double zetam = one - zeta, zetap = zeta + one;
    if (nod == 8) {
      fun[0] = pt125 * xim * etam * zetam;
      fun[1] = pt125 * xim * etam * zetap;
      fun[2] = pt125 * xip * etam * zetap;
      fun[3] = pt125 * xip * etam * zetam;
      fun[4] = pt125 * xim * etap * zetam;
      fun[5] = pt125 * xim * etap * zetap;
      fun[6] = pt125 * xip * etap * zetap;
      fun[7] = pt125 * xip * etap * zetam;
    } else {
      throw std::runtime_error("shape_fun: unsupported nod for 3D");
    }
  } else {
    throw std::runtime_error("shape_fun: unsupported ndim");
  }
}

} // namespace fem
