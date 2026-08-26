#ifndef MESH2D_GRADED_SURFACE_MESH_HPP
#define MESH2D_GRADED_SURFACE_MESH_HPP

#include "mesh2d_types.hpp"
#include <vector>

namespace mesh2d {

struct SurfaceMeshResult {
  std::vector<Point2> nodes;
  std::vector<Tri3> triangles; // 0-based node indices into nodes
  double min_angle_deg = 0;
  double max_aspect_ratio = 0;
};

/// Graded Delaunay mesh inside one closed polygon (optional holes subtracted by centroid test).
SurfaceMeshResult graded_surface_generate(
    const std::vector<Point2>& polygon,
    double near_boundary_size,
    double global_size,
    int boundary_layers,
    double growth_factor,
    int max_nodes = 20000,
    const std::vector<std::vector<Point2>>* holes = nullptr);

/// One conforming triangulation; each result shares the same \c nodes vector (same pointer data).
struct ConformingRegionResult {
  int surface_number = 0;
  SurfaceMeshResult mesh;
};

/// \p regions: (surface_number, polygon, near_size, global_size, layers, growth).
/// \p hole_only: excavated voids — boundary sampled, interior carved from parents.
void graded_surface_generate_conforming(
    const std::vector<std::tuple<int, std::vector<Point2>, double, double, int, double>>& regions,
    const std::vector<std::tuple<int, std::vector<Point2>, double>>* hole_only,
    int max_nodes,
    std::vector<ConformingRegionResult>& out);

} // namespace mesh2d

#endif
