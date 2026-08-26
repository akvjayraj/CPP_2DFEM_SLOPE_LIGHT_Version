#ifndef MAIN_BANDWIDTH_HPP
#define MAIN_BANDWIDTH_HPP

#include <vector>

namespace fem {

/// Returns the maximum |g(i)-g(j)| over active DOFs (g > 0). Used for band size.
int bandwidth(const std::vector<int>& g);

} // namespace fem

#endif
