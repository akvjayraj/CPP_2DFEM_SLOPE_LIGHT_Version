#ifndef MAIN_DETERMINANT_HPP
#define MAIN_DETERMINANT_HPP

#include <vector>

namespace fem {

/// Returns the determinant of a 1x1, 2x2 or 3x3 Jacobian matrix.
double determinant(const std::vector<std::vector<double>>& jac);

} // namespace fem

#endif
