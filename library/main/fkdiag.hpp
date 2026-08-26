#ifndef MAIN_FKDIAG_HPP
#define MAIN_FKDIAG_HPP

#include <vector>

namespace fem {

/// Computes the skyline profile kdiag.
/// kdiag is 1-indexed: kdiag[i] for equation i (1..neq)
void fkdiag(std::vector<int>& kdiag, const std::vector<int>& g, int neq);

} // namespace fem

#endif
