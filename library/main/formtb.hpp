#ifndef MAIN_FORMTB_HPP
#define MAIN_FORMTB_HPP

#include <vector>

namespace fem {

/// Assembles unsymmetric band matrix pb from element matrix km.
/// pb(row, col_in_band) with col_in_band = g(j)-g(i)+iw+1 (0-based: g(j)-g(i)+iw).
void formtb(std::vector<std::vector<double>>& pb, const std::vector<std::vector<double>>& km, const std::vector<int>& g);

} // namespace fem

#endif
