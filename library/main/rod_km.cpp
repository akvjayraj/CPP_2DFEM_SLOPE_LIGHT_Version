#include "rod_km.hpp"
#include <cmath>

namespace fem {

void rod_km(double km[2][2], double ea, double length) {
  double scale = ea / length;
  km[0][0] = scale;
  km[0][1] = -scale;
  km[1][0] = -scale;
  km[1][1] = scale;
}

} // namespace fem
