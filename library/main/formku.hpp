#ifndef MAIN_FORMKU_HPP
#define MAIN_FORMKU_HPP

#include <vector>

namespace fem {

/// Assembles element matrix km into symmetric global upper-band matrix ku.
/// g: equation numbers (1-based); ku(row, col) stored as ku[row][col] with col = column index in band (1-based in Fortran).
void formku(std::vector<std::vector<double>>& ku, const std::vector<std::vector<double>>& km, const std::vector<int>& g);

/// Overload for 4x4 element matrix (e.g. beam element).
void formku(std::vector<std::vector<double>>& ku, const double km[4][4], const std::vector<int>& g);

} // namespace fem

#endif
