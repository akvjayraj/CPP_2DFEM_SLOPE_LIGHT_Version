#ifndef MAIN_INVERT_HPP
#define MAIN_INVERT_HPP

#include <vector>

namespace fem {

/// Inverts a small square matrix (2x2 or 3x3) in place.
void invert(std::vector<std::vector<double>>& matrix);

} // namespace fem

#endif
