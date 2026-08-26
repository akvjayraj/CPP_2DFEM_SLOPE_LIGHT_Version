#ifndef MAIN_SPARIN_HPP
#define MAIN_SPARIN_HPP

#include <vector>

namespace fem {

/// Cholesky factorization of symmetric skyline matrix (in-place).
void sparin(std::vector<double>& kv, const std::vector<int>& kdiag);

} // namespace fem

#endif
