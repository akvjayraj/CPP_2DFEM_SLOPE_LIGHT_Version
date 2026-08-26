#include "vmflow.hpp"

namespace fem {

static const double onept5 = 1.5, two = 2.0, d3 = 3.0;

void vmflow(const std::vector<double>& stress, double dsbar, std::vector<double>& vmfl) {
  double sigm = (stress[0] + stress[1] + stress[3]) / d3;
  vmfl[0] = stress[0] - sigm;
  vmfl[1] = stress[1] - sigm;
  vmfl[2] = stress[2] * two;
  vmfl[3] = stress[3] - sigm;
  double scale = onept5 / dsbar;
  for (size_t i = 0; i < vmfl.size(); ++i) vmfl[i] *= scale;
}

} // namespace fem
