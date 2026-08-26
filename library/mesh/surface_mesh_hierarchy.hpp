#ifndef MESH2D_SURFACE_MESH_HIERARCHY_HPP
#define MESH2D_SURFACE_MESH_HIERARCHY_HPP

#include "mesh2d_types.hpp"
#include <map>
#include <vector>

namespace mesh2d {

double surface_polygon_area(const std::vector<Point2>& poly);
Point2 surface_polygon_centroid(const std::vector<Point2>& poly);

/// Parent surface number for each surface, or absent / -1 if outermost (no enclosing polygon).
/// Parent is the smallest-area other polygon whose interior contains this polygon's centroid.
void surface_build_parent_map(
    const std::vector<std::pair<int, std::vector<Point2>>>& surfaces,
    std::map<int, int>& parent_of);

} // namespace mesh2d

#endif
