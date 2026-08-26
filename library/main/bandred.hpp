#ifndef MAIN_BANDRED_HPP
#define MAIN_BANDRED_HPP

#include <vector>

namespace fem {

/// Reduces symmetric band matrix (upper band storage) to tridiagonal form.
/// ku: neq rows, (nband+1) columns; ku[i][k] = A(i, i+k) for 0 <= k <= nband.
/// On return: diag[1..neq] = diagonal of tridiagonal, udiag[1..neq] = off-diagonal
/// (udiag[1] unused, udiag[i] = element (i-1,i) for i>=2).
void bandred(const std::vector<std::vector<double>>& ku,
             std::vector<double>& diag,
             std::vector<double>& udiag);

} // namespace fem

#endif
