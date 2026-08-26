#include "surface_mesh_hierarchy.hpp"
#include "mesh2d_geometry.hpp"
#include <limits>

namespace mesh2d {

double surface_polygon_area(const std::vector<Point2>& poly) { return polygon_area(poly); }

Point2 surface_polygon_centroid(const std::vector<Point2>& poly) { return polygon_centroid(poly); }

void surface_build_parent_map(
    const std::vector<std::pair<int, std::vector<Point2>>>& surfaces,
    std::map<int, int>& parent_of) {
  parent_of.clear();
  struct Entry {
    int sn;
    std::vector<Point2> poly;
    double area;
  };
  std::vector<Entry> with_area;
  with_area.reserve(surfaces.size());
  for (const auto& s : surfaces) {
    Entry e;
    e.sn = s.first;
    e.poly = s.second;
    e.area = polygon_area(e.poly);
    with_area.push_back(e);
  }

  for (const auto& cur : with_area) {
    Point2 c = polygon_centroid(cur.poly);
    int best_parent = -1;
    double best_area = std::numeric_limits<double>::max();
    for (const auto& o : with_area) {
      if (o.sn == cur.sn) continue;
      if (!point_in_polygon(c, o.poly)) continue;
      if (o.area < best_area) {
        best_area = o.area;
        best_parent = o.sn;
      }
    }
    if (best_parent >= 0) parent_of[cur.sn] = best_parent;
  }
}

} // namespace mesh2d
