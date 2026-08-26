#ifndef MAIN_CHECON_HPP
#define MAIN_CHECON_HPP

#include <vector>

namespace fem {

/// Sets converged to false if relative change in loads and oldlds > tol.
/// Updates oldlds = loads.
bool checon(const std::vector<double>& loads, std::vector<double>& oldlds,
            double tol);

} // namespace fem

#endif
