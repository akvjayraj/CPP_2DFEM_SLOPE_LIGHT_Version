#include "stiff4.hpp"

namespace fem {

namespace {

constexpr double pt5 = 0.5;
constexpr double one = 1.0;
constexpr double two = 2.0;
constexpr double d3 = 3.0;
constexpr double zero = 0.0;

void groupa(double x1, double x2, double x3, double x4,
            double y1, double y2, double y3, double y4,
            double& s1, double& s2, double& s3, double& s4,
            double& t1, double& t2, double& t3, double& t4,
            double& f1, double& f2) {
  s1 = two * (y4 - y2) * (y4 - y2);
  s2 = two * (x4 - x2) * (x4 - x2);
  s3 = -s1 / two;
  s4 = -s2 / two;
  t1 = (y2 - y3) * (y2 - y3) + (y3 - y4) * (y3 - y4) + (y4 - y2) * (y4 - y2);
  t2 = (x2 - x3) * (x2 - x3) + (x3 - x4) * (x3 - x4) + (x4 - x2) * (x4 - x2);
  t3 = (y4 - y3) * (y4 - y3) - (y3 - y2) * (y3 - y2);
  t4 = (x4 - x3) * (x4 - x3) - (x3 - x2) * (x3 - x2);
  f1 = (x1 + x3) * (y4 - y2) - (y1 + y3) * (x4 - x2) - two * (x2 * y4 - x4 * y2);
  f2 = (y2 + y4) * (x3 - x1) - (x2 + x4) * (y3 - y1) - two * (x3 * y1 - x1 * y3);
}

void groupb(double x1, double x2, double x3, double x4,
            double y1, double y2, double y3, double y4,
            double& s1, double& s2, double& s3, double& s4,
            double& t1, double& t2, double& t3, double& t4,
            double& f1, double& f2) {
  s1 = two * (x2 - x4) * (y4 - y2);
  s2 = s1;
  s3 = -s1 / 2.0;
  s4 = s3;
  t1 = x2 * (y4 - two * y2 + y3) + x3 * (y2 - two * y3 + y4) + x4 * (y2 - two * y4 + y3);
  t2 = t1;
  t3 = x2 * (y2 - y3) + x3 * (y4 - y2) + x4 * (y3 - y4);
  t4 = t3;
  f1 = (x1 + x3) * (y4 - y2) - (y1 + y3) * (x4 - x2) - two * (x2 * y4 - x4 * y2);
  f2 = (y2 + y4) * (x3 - x1) - (x2 + x4) * (y3 - y1) - two * (x3 * y1 - x1 * y3);
}

void groupc(double x1, double x2, double x3, double x4,
            double y1, double y2, double y3, double y4,
            double& s1, double& s2, double& s3, double& s4,
            double& t1, double& t2, double& t3, double& t4,
            double& f1, double& f2) {
  s1 = (y4 - y2) * (two * y1 - y3 - y4);
  s2 = (x4 - x2) * (two * x1 - x3 - x4);
  s3 = (y4 - y2) * (y4 - y1);
  s4 = (x4 - x2) * (x4 - x1);
  t1 = (y3 - y1) * (two * y2 - y3 - y4);
  t2 = (x3 - x1) * (two * x2 - x3 - x4);
  t3 = (y3 - y1) * (y3 - y2);
  t4 = (x3 - x1) * (x3 - x2);
  f1 = (x1 + x3) * (y4 - y2) - (y1 + y3) * (x4 - x2) - two * (x2 * y4 - x4 * y2);
  f2 = (y2 + y4) * (x3 - x1) - (x2 + x4) * (y3 - y1) - two * (x3 * y1 - x1 * y3);
}

void groupd(double x1, double x2, double x3, double x4,
            double y1, double y2, double y3, double y4,
            double& s1, double& s2, double& s3, double& s4,
            double& t1, double& t2, double& t3, double& t4) {
  s1 = (x3 - x1) * (y4 - y2) + (x4 - x1) * (y4 - y2);
  s2 = (y3 - y1) * (x4 - x2) + (y4 - y1) * (x4 - x2);
  s3 = (x4 - x1) * (y2 - y4);
  s4 = (y4 - y1) * (x2 - x4);
  t1 = (x3 - x1) * (y4 - y2) + (x3 - x1) * (y3 - y2);
  t2 = (y3 - y1) * (x4 - x2) + (y3 - y1) * (x3 - x2);
  t3 = (x3 - x1) * (y2 - y3);
  t4 = (y3 - y1) * (x2 - x3);
}

void groupe(double x1, double x2, double x3, double x4,
            double y1, double y2, double y3, double y4,
            double& s1, double& s2, double& s3, double& s4,
            double& t1, double& t2, double& t3, double& t4,
            double& f1, double& f2) {
  s1 = -(y4 - y2) * (y4 - y2);
  s2 = -(x4 - x2) * (x4 - x2);
  s3 = zero;
  s4 = zero;
  t1 = (y3 + y1) * (y4 + y2) - two * (y4 - y2) * (y4 - y2) - two * (y1 * y3 + y2 * y4);
  t2 = (x3 + x1) * (x4 + x2) - two * (x4 - x2) * (x4 - x2) - two * (x1 * x3 + x2 * x4);
  t3 = (y4 - y2) * (y1 - y2 + y3 - y4);
  t4 = (x4 - x2) * (x1 - x2 + x3 - x4);
  f1 = (x1 + x3) * (y4 - y2) - (y1 + y3) * (x4 - x2) - two * (x2 * y4 - x4 * y2);
  f2 = (y2 + y4) * (x3 - x1) - (x2 + x4) * (y3 - y1) - two * (x3 * y1 - x1 * y3);
}

void groupf(double x1, double x2, double x3, double x4,
            double y1, double y2, double y3, double y4,
            double& s1, double& s2, double& s3, double& s4,
            double& t1, double& t2, double& t3, double& t4) {
  s1 = (x4 - x2) * (y4 - y2);
  s2 = s1;
  s3 = zero;
  s4 = zero;
  t1 = (x4 - x2) * (y4 - y2) + (x2 - x1) * (y2 - y3) + (x4 - x1) * (y4 - y3);
  t2 = (y4 - y2) * (x4 - x2) + (y2 - y1) * (x2 - x3) + (y4 - y1) * (x4 - x3);
  t3 = (x2 - x1) * (y3 - y2) + (x4 - x1) * (y4 - y3);
  t4 = (y2 - y1) * (x3 - x2) + (y4 - y1) * (x4 - x3);
}

void f1f2(double x1, double x2, double x3, double x4,
          double y1, double y2, double y3, double y4,
          double& f1, double& f2) {
  f1 = (x1 + x3) * (y4 - y2) - (y1 + y3) * (x4 - x2) - two * (x2 * y4 - x4 * y2);
  f2 = (y2 + y4) * (x3 - x1) - (x2 + x4) * (y3 - y1) - two * (x3 * y1 - x1 * y3);
}

}  // anonymous namespace

void stiff4(std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& coord,
            double ym, double pr) {
  double e1 = ym * (one - pr) / (one + pr) / (one - two * pr);
  double e2 = pr * e1 / (one - pr);
  double g = ym / two / (one + pr);

  double x1 = coord[0][0], x2 = coord[1][0], x3 = coord[2][0], x4 = coord[3][0];
  double y1 = coord[0][1], y2 = coord[1][1], y3 = coord[2][1], y4 = coord[3][1];

  double a2 = (x4 - x2) * (y3 - y1) - (x3 - x1) * (y4 - y2);
  double a2st3 = d3 * a2 * a2;

  double s1, s2, s3, s4, t1, t2, t3, t4, f1, f2;
  double alph, beta;

  // groupa calls -> diagonal and c11, c22, c33, c44, c55, c66, c77, c88
  groupa(x1, x2, x3, x4, y1, y2, y3, y4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[0][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(x2, x3, x4, x1, y2, y3, y4, y1, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[2][2] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(x3, x4, x1, x2, y3, y4, y1, y2, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[4][4] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(x4, x1, x2, x3, y4, y1, y2, y3, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[6][6] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(y4, y3, y2, y1, x4, x3, x2, x1, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[7][7] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(y1, y4, y3, y2, x1, x4, x3, x2, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[1][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(y2, y1, y4, y3, x2, x1, x4, x3, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[3][3] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupa(y3, y2, y1, y4, x3, x2, x1, x4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[5][5] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  // groupb calls
  groupb(x1, x2, x3, x4, y1, y2, y3, y4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[1][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupb(x2, x3, x4, x1, y2, y3, y4, y1, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[3][2] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupb(x3, x4, x1, x2, y3, y4, y1, y2, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[5][4] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupb(x4, x1, x2, x3, y4, y1, y2, y3, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[7][6] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  // groupc calls
  groupc(x1, x2, x3, x4, y1, y2, y3, y4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[2][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(x2, x3, x4, x1, y2, y3, y4, y1, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[4][2] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(x3, x4, x1, x2, y3, y4, y1, y2, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[6][4] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(x4, x1, x2, x3, y4, y1, y2, y3, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[6][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(y4, y3, y2, y1, x4, x3, x2, x1, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[7][5] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(y1, y4, y3, y2, x1, x4, x3, x2, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[7][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(y2, y1, y4, y3, x2, x1, x4, x3, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[3][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupc(y3, y2, y1, y4, x3, x2, x1, x4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[5][3] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  // groupd + f1f2 calls
  groupd(x1, x2, x3, x4, y1, y2, y3, y4, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x1, x2, x3, x4, y1, y2, y3, y4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[3][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(x2, x3, x4, x1, y2, y3, y4, y1, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x2, x3, x4, x1, y2, y3, y4, y1, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[5][2] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(x3, x4, x1, x2, y3, y4, y1, y2, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x3, x4, x1, x2, y3, y4, y1, y2, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[7][4] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(x4, x1, x2, x3, y4, y1, y2, y3, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x4, x1, x2, x3, y4, y1, y2, y3, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[6][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(y1, y2, y3, y4, x1, x2, x3, x4, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x1, x2, x3, x4, y1, y2, y3, y4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[2][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(y2, y3, y4, y1, x2, x3, x4, x1, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x2, x3, x4, x1, y2, y3, y4, y1, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[4][3] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(y3, y4, y1, y2, x3, x4, x1, x2, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x3, x4, x1, x2, y3, y4, y1, y2, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[6][5] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupd(y4, y1, y2, y3, x4, x1, x2, x3, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x4, x1, x2, x3, y4, y1, y2, y3, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[7][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  // groupe calls
  groupe(x1, x2, x3, x4, y1, y2, y3, y4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[4][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupe(x2, x3, x4, x1, y2, y3, y4, y1, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[6][2] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupe(y2, y1, y4, y3, x2, x1, x4, x3, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[7][3] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupe(y3, y2, y1, y4, x3, x2, x1, x4, s1, s2, s3, s4, t1, t2, t3, t4, f1, f2);
  alph = a2 * (s1 * e1 + s2 * g) + f1 * (s3 * e1 + s4 * g);
  beta = a2 * (t1 * e1 + t2 * g) + f2 * (t3 * e1 + t4 * g);
  km[5][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  // groupf + f1f2 calls
  groupf(x1, x2, x3, x4, y1, y2, y3, y4, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x1, x2, x3, x4, y1, y2, y3, y4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[5][0] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupf(x2, x3, x4, x1, y2, y3, y4, y1, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x2, x3, x4, x1, y2, y3, y4, y1, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[7][2] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupf(y1, y2, y3, y4, x1, x2, x3, x4, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x1, x2, x3, x4, y1, y2, y3, y4, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[4][1] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  groupf(y2, y3, y4, y1, x2, x3, x4, x1, s1, s2, s3, s4, t1, t2, t3, t4);
  f1f2(x2, x3, x4, x1, y2, y3, y4, y1, f1, f2);
  alph = a2 * (s1 * e2 + s2 * g) + f1 * (s3 * e2 + s4 * g);
  beta = a2 * (t1 * e2 + t2 * g) + f2 * (t3 * e2 + t4 * g);
  km[6][3] = (alph / (a2st3 - f1 * f1) + beta / (a2st3 - f2 * f2)) * pt5;

  // Fill upper triangle from lower: km(i,j) = km(j,i) for j > i
  for (int i = 0; i < 8; ++i) {
    for (int j = i + 1; j < 8; ++j) {
      km[i][j] = km[j][i];
    }
  }
}

}  // namespace fem
