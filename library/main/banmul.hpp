#ifndef MAIN_BANMUL_HPP
#define MAIN_BANMUL_HPP

#include <vector>

namespace fem {

/// Symmetric band matrix kb times vector loads -> ans. kb(neq, nband+1), 1-based column in band.
void banmul(const std::vector<std::vector<double>>& kb, const std::vector<double>& loads, std::vector<double>& ans);

} // namespace fem

#endif
