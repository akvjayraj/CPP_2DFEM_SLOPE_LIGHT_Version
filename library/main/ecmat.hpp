#ifndef MAIN_ECMAT_HPP
#define MAIN_ECMAT_HPP

#include <vector>

namespace fem {

/// Forms element consistent mass matrix: ecm = N^T * N (scaled by shape funs).
/// fun has nod values; ecm is ndof x ndof; ndof = nod * nodof.
void ecmat(std::vector<std::vector<double>>& ecm, const std::vector<double>& fun, int ndof, int nodof);

} // namespace fem

#endif
