#ifndef MAIN_FORMLUMP_HPP
#define MAIN_FORMLUMP_HPP

#include <vector>

namespace fem {

/// Forms lumped global mass diagonal from element mass matrix emm.
/// diag[g(i)-1] += emm(i,i). g is 1-based equation numbers.
void formlump(std::vector<double>& diag, const std::vector<std::vector<double>>& emm, const std::vector<int>& g);

/// Row-sum lumping: diag[g(i)] += sum_j mm(i,j). diag is 1-based (size neq+1).
void formlump_rowsum(std::vector<double>& diag, const std::vector<std::vector<double>>& mm, const std::vector<int>& g);
/// Row-sum lumping for 4x4 mass matrix.
void formlump_rowsum(std::vector<double>& diag, const double mm[4][4], const std::vector<int>& g);

} // namespace fem

#endif
