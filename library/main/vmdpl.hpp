#ifndef MAIN_VMDPL_HPP
#define MAIN_VMDPL_HPP

#include <vector>

namespace fem {

/// Von Mises plastic stress/strain matrix pl from dee and stress.
void vmdpl(const std::vector<std::vector<double>>& dee, const std::vector<double>& stress,
           std::vector<std::vector<double>>& pl);

} // namespace fem

#endif
