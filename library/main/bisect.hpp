#ifndef MAIN_BISECT_HPP
#define MAIN_BISECT_HPP

#include <vector>

namespace fem {

/// Computes eigenvalues of symmetric tridiagonal matrix by bisection (Sturm sequence).
/// diag[1..neq] = diagonal, udiag[1..neq] = off-diagonal (udiag[1] unused).
/// On return: diag[1..neq] overwritten with eigenvalues in ascending order.
/// ifail: 0 = success, nonzero = error.
void bisect(std::vector<double>& diag,
            const std::vector<double>& udiag,
            double etol,
            int& ifail);

} // namespace fem

#endif
