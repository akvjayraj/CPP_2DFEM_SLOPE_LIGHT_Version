#ifndef MAIN_STIFF10_HPP
#define MAIN_STIFF10_HPP

#include <vector>

namespace fem {

/// 10-node triangle plane strain stiffness (coord 10x2, km 20x20). Stub: zeros; full port from Fortran available.
void stiff10(std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& coord, double ym, double pr);

} // namespace fem

#endif
