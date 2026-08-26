#ifndef MAIN_SPABAC_GAUSS_HPP
#define MAIN_SPABAC_GAUSS_HPP

#include <vector>

namespace fem {

/// Solves (LDL^T)*x = loads using factorization from sparin_gauss.
/// loads is overwritten with the solution.
void spabac_gauss(const std::vector<double>& kv, std::vector<double>& loads,
                  const std::vector<int>& kdiag);

} // namespace fem

#endif
