#ifndef MESH2D_GEOM_RECT_TRIANGLE_HPP
#define MESH2D_GEOM_RECT_TRIANGLE_HPP

#include "mesh2d_types.hpp"
#include <vector>

namespace mesh2d {

struct GeomRectTriangleMeshResult {
  int nxe = 0;
  int nye = 0;
  /// Index 0 = global node 1, … (axis coordinates).
  std::vector<Point2> node_points;
  /// Per-element global node numbers (1-based), same as PFEM geom_rect / C# builder.
  std::vector<Tri3> triangles_1based;
};

int geom_rect_element_count(int nxe, int nye_physical);
int geom_rect_node_count(int nxe, int nye_physical);

void geom_rect_get_triangle_node_ids(int iel, int nxe, int nye_physical, char dir, int& n1, int& n2, int& n3);

/// Builds structured triangle mesh; throws no exceptions — on invalid input leaves \p out empty.
void geom_rect_triangle_build(const std::vector<double>& x_coords, const std::vector<double>& y_coords,
                               char dir, GeomRectTriangleMeshResult& out);

} // namespace mesh2d

#endif
