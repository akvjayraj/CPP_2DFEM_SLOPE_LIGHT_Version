#ifndef GEOM_EMB_2D_GEOM_HPP
#define GEOM_EMB_2D_GEOM_HPP

#include <vector>

namespace geom {

/// Forms nodal coordinates and numbering for 2D slope of 8-node quads.
/// Nodes numbered in y-direction, elements in x-direction.
void emb_2d_geom(int iel, int nx1, int nx2, int ny1, int ny2,
                 double w1, double s1, double w2, double h1, double h2,
                 std::vector<std::vector<double>>& coord,
                 std::vector<int>& num);

} // namespace geom

#endif
