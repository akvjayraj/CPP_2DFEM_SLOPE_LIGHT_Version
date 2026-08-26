#ifndef GEOM_BC_RECT_HPP
#define GEOM_BC_RECT_HPP

#include <vector>

namespace geom {

/// Generates nf array for rectangular mesh of 8-node quadrilaterals:
/// fully fixed on base, vertical rollers on left/right.
/// Nodes numbered in x- or y-direction. dir='x' or 'y'.
/// nf: column-major (nodof x nn), 0 = fixed.
void bc_rect(int nxe, int nye, std::vector<int>& nf, char dir);

} // namespace geom

#endif
