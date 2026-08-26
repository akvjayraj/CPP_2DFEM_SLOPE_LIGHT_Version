#include "ecmat.hpp"

namespace fem {

void ecmat(std::vector<std::vector<double>>& ecm, const std::vector<double>& fun, int ndof, int nodof) {
  const int nod = ndof / nodof;
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < ndof; ++j)
      ecm[i][j] = 0.0;
  // nt(ndof, nodof): nt((i-1)*nodof+j, j) = fun(i)
  // ecm = nt * tn, tn = nt^T
  for (int i = 0; i < nod; ++i) {
    for (int j = 0; j < nodof; ++j) {
      int row = i * nodof + j;
      double f = fun[i];
      for (int k = 0; k < nod; ++k) {
        int col = k * nodof + j;
        ecm[row][col] = f * fun[k];
      }
    }
  }
}

} // namespace fem
