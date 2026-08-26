#ifndef MAIN_RIGID_JOINTED_HPP
#define MAIN_RIGID_JOINTED_HPP

#include <vector>

namespace fem {

/// Forms the stiffness matrix of a general beam/column element (1-, 2- or 3-d).
/// coord: (nod x ndim), prop: (nprops x np_types), gamma: (nels).
void rigid_jointed(std::vector<std::vector<double>>& km,
                   const std::vector<std::vector<double>>& prop,
                   const std::vector<double>& gamma,
                   const std::vector<int>& etype, int iel,
                   const std::vector<std::vector<double>>& coord);

} // namespace fem

#endif
