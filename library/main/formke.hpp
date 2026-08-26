#ifndef MAIN_FORMKE_HPP
#define MAIN_FORMKE_HPP

#include <vector>

namespace fem {

/// Forms coupled ke matrix for incremental Biot: ke = [km c; c' -theta*kp].
void formke(const std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& kp,
            const std::vector<std::vector<double>>& c, double theta, std::vector<std::vector<double>>& ke);

} // namespace fem

#endif
