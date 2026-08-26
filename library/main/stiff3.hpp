#ifndef MAIN_STIFF3_HPP
#define MAIN_STIFF3_HPP

#include <vector>

namespace fem {

/// Analytical stiffness matrix of 3-node triangle in plane strain.
/// coord: 3x2 (row per node, x,y). km: 6x6.
void stiff3(std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& coord,
            double ym, double pr);

} // namespace fem

#endif
