#ifndef MAIN_SOLVE_BAND_HPP
#define MAIN_SOLVE_BAND_HPP

#include <vector>

namespace fem {

/// Forward and back substitution using reduced band matrix from gauss_band.
void solve_band(const std::vector<std::vector<double>>& pb, const std::vector<std::vector<double>>& work,
                std::vector<double>& loads);

} // namespace fem

#endif
