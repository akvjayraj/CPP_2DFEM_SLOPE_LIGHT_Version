#include "stiff10.hpp"

namespace fem {

void stiff10(std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& coord, double ym, double pr) {
  (void)coord;
  (void)ym;
  (void)pr;
  for (size_t i = 0; i < km.size(); ++i)
    for (size_t j = 0; j < km[0].size(); ++j) km[i][j] = 0.0;
}

} // namespace fem
