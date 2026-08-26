#ifndef MAIN_RECT_KM_HPP
#define MAIN_RECT_KM_HPP

#include <vector>

namespace fem {

/// Forms analytical stiffness matrix for 4- or 8-node plane strain elements.
/// coord: nod x 2 (row per node, x,y). km: nod*2 x nod*2.
void rect_km(std::vector<std::vector<double>>& km,
             const std::vector<std::vector<double>>& coord,
             double e, double v);

} // namespace fem

#endif
