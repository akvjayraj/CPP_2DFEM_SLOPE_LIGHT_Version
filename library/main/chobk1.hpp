#ifndef MAIN_CHOBK1_HPP
#define MAIN_CHOBK1_HPP

#include <vector>

namespace fem {

/// Cholesky forward substitution. loads has size n+1 (indices 0..n).
void chobk1(const std::vector<std::vector<double>>& kb,
            std::vector<double>& loads);

} // namespace fem

#endif
