#ifndef GEOM_EMB_2D_BC_HPP
#define GEOM_EMB_2D_BC_HPP

#include <vector>

namespace geom {

/// Generates nf array for 2D slope geometry (8-node quads).
void emb_2d_bc(int nx1, int nx2, int ny1, int ny2, std::vector<int>& nf);

} // namespace geom

#endif
