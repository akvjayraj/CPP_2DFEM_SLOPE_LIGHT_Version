#ifndef MAIN_CHOLIN_HPP
#define MAIN_CHOLIN_HPP

#include <vector>

namespace fem {

/// Cholesky reduction on band matrix kb(n, iw+1) stored as lower triangle.
/// kb is row-major: kb[i][j] = row i, col j.
void cholin(std::vector<std::vector<double>>& kb);

} // namespace fem

#endif
