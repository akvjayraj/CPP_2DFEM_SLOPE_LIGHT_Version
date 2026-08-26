#ifndef MAIN_STIFF4_HPP
#define MAIN_STIFF4_HPP

#include <vector>

namespace fem {

/// Analytical stiffness matrix for 4-node quadrilateral in plane strain (nip=4).
void stiff4(std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& coord,
            double ym, double pr);

} // namespace fem

#endif
