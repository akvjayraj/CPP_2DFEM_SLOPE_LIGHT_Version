#ifndef MAIN_MCDPL_HPP
#define MAIN_MCDPL_HPP

#include <vector>

namespace fem {

/// Mohr-Coulomb plastic stress/strain matrix (phi, psi in degrees). 4 or 6 stress components.
void mcdpl(double phi, double psi, const std::vector<std::vector<double>>& dee,
           const std::vector<double>& stress, std::vector<std::vector<double>>& pl);

} // namespace fem

#endif
