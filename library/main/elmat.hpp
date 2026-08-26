#ifndef MAIN_ELMAT_HPP
#define MAIN_ELMAT_HPP

#include <vector>

namespace fem {

/// Forms lumped mass matrix for 4-node (ndof=8) or 8-node (ndof=16) plane strain element.
/// emm is overwritten; diagonal entries only (lumped).
void elmat(double area, double rho, std::vector<std::vector<double>>& emm);

} // namespace fem

#endif
