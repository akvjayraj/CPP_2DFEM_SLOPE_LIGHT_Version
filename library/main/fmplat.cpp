#include "fmplat.hpp"

namespace fem {

void fmplat(const std::vector<std::vector<double>>& points, double aa, double bb,
            int i, std::vector<double>& d2x, std::vector<double>& d2y,
            std::vector<double>& d2xy) {
  const double pt25 = 0.25, pt375 = 0.375, pt5 = 0.5, pt75 = 0.75;
  const double one = 1.0, opt5 = 1.5, d3 = 3.0;
  double x = points[i - 1][0], e = points[i - 1][1];
  double xp1 = x + one, xp12 = xp1 * xp1, xp13 = xp12 * xp1;
  double ep1 = e + one, ep12 = ep1 * ep1, ep13 = ep12 * ep1;
  double p1 = one - pt75 * xp12 + pt25 * xp13;
  double q1 = one - pt75 * ep12 + pt25 * ep13;
  double p2 = pt5 * aa * xp1 * (one - xp1 + pt25 * xp12);
  double q2 = pt5 * bb * ep1 * (one - ep1 + pt25 * ep12);
  double p3 = pt25 * xp12 * (d3 - xp1);
  double q3 = pt25 * ep12 * (d3 - ep1);
  double p4 = pt25 * aa * xp12 * (pt5 * xp1 - one);
  double q4 = pt25 * bb * ep12 * (pt5 * ep1 - one);
  double dp1 = opt5 * xp1 * (pt5 * xp1 - one);
  double dq1 = opt5 * ep1 * (pt5 * ep1 - one);
  double dp2 = aa * (pt5 - xp1 + pt375 * xp12);
  double dq2 = bb * (pt5 - ep1 + pt375 * ep12);
  double dp3 = opt5 * xp1 * (one - pt5 * xp1);
  double dq3 = opt5 * ep1 * (one - pt5 * ep1);
  double dp4 = pt5 * aa * xp1 * (pt75 * xp1 - one);
  double dq4 = pt5 * bb * ep1 * (pt75 * ep1 - one);
  double d2p1 = opt5 * x, d2p2 = pt25 * aa * (d3 * x - one), d2p3 = -d2p1;
  double d2p4 = pt25 * aa * (d3 * x + one);
  double d2q1 = opt5 * e, d2q2 = pt25 * bb * (d3 * e - one), d2q3 = -d2q1;
  double d2q4 = pt25 * bb * (d3 * e + one);
  d2x[0] = d2p1 * q1;  d2x[1] = d2p2 * q1;  d2x[2] = d2p1 * q2;  d2x[3] = d2p2 * q2;
  d2x[4] = d2p1 * q3;  d2x[5] = d2p2 * q3;  d2x[6] = d2p1 * q4;  d2x[7] = d2p2 * q4;
  d2x[8] = d2p3 * q3;  d2x[9] = d2p4 * q3;  d2x[10] = d2p3 * q4; d2x[11] = d2p4 * q4;
  d2x[12] = d2p3 * q1; d2x[13] = d2p4 * q1; d2x[14] = d2p3 * q2; d2x[15] = d2p4 * q2;
  d2y[0] = p1 * d2q1;  d2y[1] = p2 * d2q1;  d2y[2] = p1 * d2q2;  d2y[3] = p2 * d2q2;
  d2y[4] = p1 * d2q3;  d2y[5] = p2 * d2q3;  d2y[6] = p1 * d2q4;  d2y[7] = p2 * d2q4;
  d2y[8] = p3 * d2q3;  d2y[9] = p4 * d2q3;  d2y[10] = p3 * d2q4; d2y[11] = p4 * d2q4;
  d2y[12] = p3 * d2q1; d2y[13] = p4 * d2q1; d2y[14] = p3 * d2q2; d2y[15] = p4 * d2q2;
  d2xy[0] = dp1 * dq1;  d2xy[1] = dp2 * dq1;  d2xy[2] = dp1 * dq2;  d2xy[3] = dp2 * dq2;
  d2xy[4] = dp1 * dq3;  d2xy[5] = dp2 * dq3;  d2xy[6] = dp1 * dq4;  d2xy[7] = dp2 * dq4;
  d2xy[8] = dp3 * dq3;  d2xy[9] = dp4 * dq3;  d2xy[10] = dp3 * dq4; d2xy[11] = dp4 * dq4;
  d2xy[12] = dp3 * dq1; d2xy[13] = dp4 * dq1; d2xy[14] = dp3 * dq2; d2xy[15] = dp4 * dq2;
}

} // namespace fem
