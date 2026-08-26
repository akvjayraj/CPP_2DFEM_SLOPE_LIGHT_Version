#ifndef MAIN_FORMM_HPP
#define MAIN_FORMM_HPP

#include <vector>

namespace fem {

/// Forms derivatives of invariants w.r.t. stress (2D nst=4 or 3D nst=6).
void formm(const std::vector<double>& stress,
           std::vector<std::vector<double>>& m1,
           std::vector<std::vector<double>>& m2,
           std::vector<std::vector<double>>& m3);

} // namespace fem

#endif
