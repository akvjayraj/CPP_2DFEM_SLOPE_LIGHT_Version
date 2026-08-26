#ifndef MESH2D_POLYGON_EAR_HPP
#define MESH2D_POLYGON_EAR_HPP

#include "mesh2d_types.hpp"
#include <vector>

namespace mesh2d {

/// Ear-clipping for a simple polygon. Returns 0-based corner indices into \p vertices.
bool polygon_ear_triangulate(const std::vector<Point2>& vertices, std::vector<Tri3>& triangles);

} // namespace mesh2d

#endif
