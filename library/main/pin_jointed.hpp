#ifndef MAIN_PIN_JOINTED_HPP
#define MAIN_PIN_JOINTED_HPP

#include <vector>

namespace fem {

/// Forms the stiffness matrix of a general rod element (1-, 2- or 3-d).
/// coord: (nod x ndim), row-major. km: (ndof x ndof), row-major.
void pin_jointed(std::vector<std::vector<double>>& km, double ea,
                 const std::vector<std::vector<double>>& coord);

} // namespace fem

#endif
