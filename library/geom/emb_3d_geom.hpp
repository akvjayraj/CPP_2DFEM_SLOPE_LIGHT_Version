#ifndef GEOM_EMB_3D_GEOM_HPP
#define GEOM_EMB_3D_GEOM_HPP

#include <vector>

namespace geom {

/// Forms nodal coordinates and numbering for 3D slope of 20-node hexahedra.
void emb_3d_geom(int iel, int nx1, int nx2, int ny1, int ny2, int nze,
                 double w1, double s1, double w2, double h1, double h2, double d1,
                 std::vector<std::vector<double>>& coord, std::vector<int>& num);

} // namespace geom

#endif
