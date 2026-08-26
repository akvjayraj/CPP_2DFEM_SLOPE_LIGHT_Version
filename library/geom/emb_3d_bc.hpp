#ifndef GEOM_EMB_3D_BC_HPP
#define GEOM_EMB_3D_BC_HPP

#include <vector>

namespace geom {

/// Generates nf array for 3D slope geometry.
/// ifix=1: smooth-smooth, ifix=2: rough-smooth, ifix=3: rough-rough.
void emb_3d_bc(int ifix, int nx1, int nx2, int ny1, int ny2, int nze,
               std::vector<int>& nf);

} // namespace geom

#endif
