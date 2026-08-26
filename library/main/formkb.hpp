#ifndef MAIN_FORMKB_HPP
#define MAIN_FORMKB_HPP

#include <vector>

namespace fem {

/// Assembles element matrix km into lower symmetric band matrix kb.
/// kb(row, col) stored so that column index in band = g(i)-g(j) (lower triangle, 0-based).
void formkb(std::vector<std::vector<double>>& kb, const std::vector<std::vector<double>>& km, const std::vector<int>& g);

} // namespace fem

#endif
