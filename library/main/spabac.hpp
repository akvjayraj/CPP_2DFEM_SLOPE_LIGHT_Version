#ifndef MAIN_SPABAC_HPP
#define MAIN_SPABAC_HPP

#include <vector>

namespace fem {

/// Cholesky forward and back-substitution (loads modified in-place).
/// loads has size neq+1 (indices 0..neq).
void spabac(const std::vector<double>& kv, std::vector<double>& loads,
            const std::vector<int>& kdiag);

} // namespace fem

#endif
