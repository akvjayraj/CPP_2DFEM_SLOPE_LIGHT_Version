#ifndef MAIN_STIFF15_HPP
#define MAIN_STIFF15_HPP

#include <vector>

namespace fem {

/// 15-node triangle plane strain stiffness (coord 15x2, km 30x30). Stub: zeros.
void stiff15(std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& coord, double ym, double pr);

} // namespace fem

#endif
