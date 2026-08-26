#ifndef MAIN_CHOBK2_HPP
#define MAIN_CHOBK2_HPP

#include <vector>

namespace fem {

/// Cholesky back substitution. loads has size n+1 (indices 0..n).
void chobk2(const std::vector<std::vector<double>>& kb,
            std::vector<double>& loads);

} // namespace fem

#endif
