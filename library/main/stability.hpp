#ifndef MAIN_STABILITY_HPP
#define MAIN_STABILITY_HPP

#include <vector>

namespace fem {

/// Computes the smallest eigenvalue in beam stability analysis.
/// Modifies kv (factorized) and gv. Returns eigenvalue in eval, eigenvector in evec.
void stability(std::vector<double>& kv, std::vector<double>& gv,
               const std::vector<int>& kdiag, double tol, int limit,
               int& iters, std::vector<double>& evec, double& eval);

} // namespace fem

#endif
