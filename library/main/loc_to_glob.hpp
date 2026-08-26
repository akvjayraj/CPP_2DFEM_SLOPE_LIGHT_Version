#ifndef MAIN_LOC_TO_GLOB_HPP
#define MAIN_LOC_TO_GLOB_HPP

#include <vector>

namespace fem {

/// Transforms local end reactions/moments (12) to global 3D using gamma (degrees) and coord(2,3).
void loc_to_glob(const std::vector<double>& local, std::vector<double>& global, double gamma,
                 const std::vector<std::vector<double>>& coord);

} // namespace fem

#endif
