#ifndef MESH2D_TUNNEL_MESH_HPP
#define MESH2D_TUNNEL_MESH_HPP

#include "mesh2d_types.hpp"
#include <vector>

namespace mesh2d {

struct TunnelMeshResult {
  std::vector<Point2> nodes;
  std::vector<Tri3> active_triangles;
  std::vector<Tri3> tunnel_triangles;
  double min_angle_deg = 0;
  double max_aspect_ratio = 0;
};

TunnelMeshResult tunnel_mesh_generate(
    const std::vector<Point2>& outer_polygon,
    const std::vector<std::pair<Point2, double>>& circle_holes,
    const std::vector<std::vector<Point2>>& polygon_holes,
    double far_field_edge_length,
    double near_boundary_size,
    int ring_count,
    double ring_growth_factor,
    double first_ring_thickness,
    int max_nodes = 60000);

} // namespace mesh2d

#endif
