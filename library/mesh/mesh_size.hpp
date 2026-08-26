#ifndef GEOM_MESH_SIZE_HPP
#define GEOM_MESH_SIZE_HPP

#include <string>

namespace geom {

/// Returns nels and nn for 2D/3D geometry-created mesh.
/// nze = -1 for 2D (optional in Fortran).
void mesh_size(const std::string& element, int nod, int nxe, int nye,
               int& nels, int& nn, int nze = -1);

} // namespace geom

#endif
