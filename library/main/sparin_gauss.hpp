#ifndef MAIN_SPARIN_GAUSS_HPP
#define MAIN_SPARIN_GAUSS_HPP

#include <vector>

namespace fem {

/// LDL^T factorization of symmetric indefinite matrix in skyline storage.
/// Same layout as sparin; diagonal holds D(i) (not sqrt). Used for shifted
/// systems (K - lambda*I) when extracting eigenvectors.
void sparin_gauss(std::vector<double>& kv, const std::vector<int>& kdiag);

} // namespace fem

#endif
