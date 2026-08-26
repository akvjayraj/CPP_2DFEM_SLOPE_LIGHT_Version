#ifndef MAIN_BEEMAT_HPP
#define MAIN_BEEMAT_HPP

#include <vector>

namespace fem {

/// Forms the bee (strain-displacement) matrix. ih=3 or 4: 2D, ih=6: 3D.
void beemat(std::vector<std::vector<double>>& bee,
            const std::vector<std::vector<double>>& deriv);

} // namespace fem

#endif
