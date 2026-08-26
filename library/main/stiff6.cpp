#include "stiff6.hpp"
#include <stdexcept>

namespace fem {

void stiff6(std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& coord,
            double ym, double pr) {
  (void)km;
  (void)coord;
  (void)ym;
  (void)pr;
  throw std::runtime_error("stiff6: full 6-node triangle stiffness not yet implemented - use numerical integration");
}

} // namespace fem
