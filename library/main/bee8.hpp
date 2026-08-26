#ifndef MAIN_BEE8_HPP
#define MAIN_BEE8_HPP

#include <vector>

namespace fem {

/// B-matrix for 8-node serendipity quad, plane strain (4 stress components). coord[dim][node], bee 4×16.
void bee8(std::vector<std::vector<double>>& bee,
          const std::vector<std::vector<double>>& coord,
          double xi, double eta, double& det);

} // namespace fem

#endif
