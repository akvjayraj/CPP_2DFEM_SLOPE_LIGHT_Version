#ifndef MAIN_HINGE_HPP
#define MAIN_HINGE_HPP

#include <vector>

namespace fem {

/// Forms the end forces and moments if a joint has gone plastic.
void hinge(const std::vector<std::vector<double>>& coord,
           const std::vector<std::vector<double>>& holdr,
           const std::vector<double>& action, std::vector<double>& react,
           const std::vector<std::vector<double>>& prop,
           const std::vector<int>& etype, int iel,
           const std::vector<double>& gamma);

} // namespace fem

#endif
