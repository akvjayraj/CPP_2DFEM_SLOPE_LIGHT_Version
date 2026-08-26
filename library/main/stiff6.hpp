#ifndef MAIN_STIFF6_HPP
#define MAIN_STIFF6_HPP

#include <vector>

namespace fem {

/// Exact stiffness matrix of 6-node triangle in plane strain.
/// coord: 6x2. km: 12x12.
void stiff6(std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& coord,
            double ym, double pr);

} // namespace fem

#endif
