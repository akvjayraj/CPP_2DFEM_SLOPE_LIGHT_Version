#ifndef MAIN_FORMUPV_HPP
#define MAIN_FORMUPV_HPP

#include <vector>

namespace fem {

/// Forms unsymmetric u-p-v Navier-Stokes element matrix from c11,c12,c21,c23,c32.
void formupv(std::vector<std::vector<double>>& ke,
             const std::vector<std::vector<double>>& c11, const std::vector<std::vector<double>>& c12,
             const std::vector<std::vector<double>>& c21, const std::vector<std::vector<double>>& c23,
             const std::vector<std::vector<double>>& c32);

} // namespace fem

#endif
