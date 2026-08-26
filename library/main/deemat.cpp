#include "deemat.hpp"

namespace fem {

void deemat(std::vector<std::vector<double>>& dee, double e, double v) {
  const double zero = 0.0, pt5 = 0.5, one = 1.0, two = 2.0;

  int ih = static_cast<int>(dee.size());
  for (size_t i = 0; i < dee.size(); ++i)
    for (size_t j = 0; j < dee[i].size(); ++j)
      dee[i][j] = zero;

  double v1 = one - v;
  double c = e / ((one + v) * (one - two * v));

  if (ih == 3) {
    dee[0][0] = v1 * c;
    dee[1][1] = v1 * c;
    dee[0][1] = v * c;
    dee[1][0] = v * c;
    dee[2][2] = pt5 * c * (one - two * v);
  } else if (ih == 4) {
    dee[0][0] = v1 * c;
    dee[1][1] = v1 * c;
    dee[3][3] = v1 * c;
    dee[2][2] = pt5 * c * (one - two * v);
    dee[0][1] = v * c;
    dee[1][0] = v * c;
    dee[0][3] = v * c;
    dee[3][0] = v * c;
    dee[1][3] = v * c;
    dee[3][1] = v * c;
  } else if (ih == 6) {
    double v2 = v / (one - v);
    double vv = (one - two * v) / (one - v) * pt5;
    for (int i = 0; i < 3; ++i) dee[i][i] = one;
    for (int i = 3; i < 6; ++i) dee[i][i] = vv;
    dee[0][1] = dee[1][0] = v2;
    dee[0][2] = dee[2][0] = v2;
    dee[1][2] = dee[2][1] = v2;
    double scale = e / (two * (one + v) * vv);
    for (size_t i = 0; i < dee.size(); ++i)
      for (size_t j = 0; j < dee[i].size(); ++j)
        dee[i][j] *= scale;
  }
}

} // namespace fem
