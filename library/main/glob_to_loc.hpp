#ifndef MAIN_GLOB_TO_LOC_HPP
#define MAIN_GLOB_TO_LOC_HPP

#include <vector>

namespace fem {

/// Transforms global reactions (6 or 12) to local 2D or 3D. coord is (2, 2) or (2, 3).
void glob_to_loc(const std::vector<double>& global, std::vector<double>& local, double gamma,
                 const std::vector<std::vector<double>>& coord);

} // namespace fem

#endif
