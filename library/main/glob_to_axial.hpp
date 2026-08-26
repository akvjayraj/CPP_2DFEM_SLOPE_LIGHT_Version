#ifndef MAIN_GLOB_TO_AXIAL_HPP
#define MAIN_GLOB_TO_AXIAL_HPP

#include <vector>

namespace fem {

/// Transforms global end reactions into axial force for rod elements (2- or 3-d).
/// global: reaction vector (size ndim*2 for 2 nodes), coord: (2 x ndim).
double glob_to_axial(const std::vector<double>& global,
                     const std::vector<std::vector<double>>& coord);

} // namespace fem

#endif
