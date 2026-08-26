#ifndef MAIN_BANTMUL_HPP
#define MAIN_BANTMUL_HPP

#include <vector>

namespace fem {

/// Multiplies unsymmetric band matrix kb by vector loads -> ans.
void bantmul(const std::vector<std::vector<double>>& kb, const std::vector<double>& loads, std::vector<double>& ans);

} // namespace fem

#endif
