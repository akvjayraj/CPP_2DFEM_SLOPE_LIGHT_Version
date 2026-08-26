#ifndef MESH2D_T6_UPGRADE_HPP
#define MESH2D_T6_UPGRADE_HPP

#include "mesh2d_types.hpp"
#include <map>
#include <utility>
#include <vector>

namespace mesh2d {

/// Appends midside nodes to \p nodes; returns T6 connectivity (0-based indices).
std::vector<Tri6> t6_upgrade_mesh(std::vector<Point2>& nodes, const std::vector<Tri3>& t3_triangles);

} // namespace mesh2d

#endif
