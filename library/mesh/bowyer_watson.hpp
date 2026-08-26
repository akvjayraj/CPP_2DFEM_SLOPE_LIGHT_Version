#ifndef MESH2D_BOWYER_WATSON_HPP
#define MESH2D_BOWYER_WATSON_HPP

#include "mesh2d_types.hpp"
#include <vector>

namespace mesh2d {

/// Incremental Bowyer–Watson Delaunay triangulation. Triangle indices are 0-based into \p input_points.
std::vector<Tri3> bowyer_watson_triangulate(const std::vector<Point2>& input_points);

} // namespace mesh2d

#endif
