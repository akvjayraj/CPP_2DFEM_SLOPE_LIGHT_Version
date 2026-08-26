#include "graded_surface_mesh.hpp"
#include "bowyer_watson.hpp"
#include "mesh2d_geometry.hpp"
#include "surface_mesh_hierarchy.hpp"
#include <algorithm>
#include <cmath>
#include <map>

namespace mesh2d {

namespace {

void add_boundary_points(const std::vector<Point2>& poly, double spacing, std::vector<Point2>& output) {
  int n = static_cast<int>(poly.size());
  for (int i = 0; i < n; ++i) {
    const Point2& a = poly[static_cast<size_t>(i)];
    const Point2& b = poly[static_cast<size_t>((i + 1) % n)];
    double len = dist2(a, b);
    if (len < 1e-10) continue;
    int segs = std::max(1, static_cast<int>(std::ceil(len / spacing)));
    for (int j = 0; j < segs; ++j) {
      double t = static_cast<double>(j) / segs;
      output.push_back({a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t});
    }
  }
}

bool compute_inward_offset_polygon(const std::vector<Point2>& poly, double dist, std::vector<Point2>& res) {
  int n = static_cast<int>(poly.size());
  res.clear();
  res.reserve(static_cast<size_t>(n));
  for (int i = 0; i < n; ++i) {
    const Point2& prev = poly[static_cast<size_t>((i + n - 1) % n)];
    const Point2& cur = poly[static_cast<size_t>(i)];
    const Point2& next = poly[static_cast<size_t>((i + 1) % n)];
    double d1x = cur.x - prev.x, d1y = cur.y - prev.y;
    normalize2(d1x, d1y);
    double n1x = d1y, n1y = -d1x;
    double d2x = next.x - cur.x, d2y = next.y - cur.y;
    normalize2(d2x, d2y);
    double n2x = d2y, n2y = -d2x;
    double bx = n1x + n2x, by = n1y + n2y;
    double b_len = std::sqrt(bx * bx + by * by);
    double px, py;
    if (b_len < 1e-8) {
      px = cur.x + n1x * dist;
      py = cur.y + n1y * dist;
    } else {
      bx /= b_len;
      by /= b_len;
      double cos_half = n1x * n2x + n1y * n2y;
      double sin_half = std::sqrt(std::max(0.0, (1 - cos_half) / 2));
      double scale = sin_half < 0.15 ? dist : dist / sin_half;
      scale = std::min(scale, dist * 3);
      px = cur.x + bx * scale;
      py = cur.y + by * scale;
    }
    res.push_back({px, py});
  }
  for (const auto& p : res) {
    if (!point_in_polygon(p, poly)) return false;
  }
  return true;
}

bool centroid_in_any_hole(const Point2& c, const std::vector<std::vector<Point2>>* holes) {
  if (!holes) return false;
  for (const auto& h : *holes) {
    if (h.size() >= 3 && point_in_polygon(c, h)) return true;
  }
  return false;
}

void add_interior_grid_points(const std::vector<Point2>& poly, double spacing, std::vector<Point2>& output,
                              int max_nodes, const std::vector<std::vector<Point2>>* holes) {
  double min_x = poly[0].x, max_x = poly[0].x, min_y = poly[0].y, max_y = poly[0].y;
  for (size_t i = 1; i < poly.size(); ++i) {
    min_x = std::min(min_x, poly[i].x);
    max_x = std::max(max_x, poly[i].x);
    min_y = std::min(min_y, poly[i].y);
    max_y = std::max(max_y, poly[i].y);
  }
  double off_x = spacing * 0.5, off_y = spacing * 0.5;
  for (double y = min_y + off_y; y < max_y && static_cast<int>(output.size()) < max_nodes; y += spacing) {
    for (double x = min_x + off_x; x < max_x && static_cast<int>(output.size()) < max_nodes; x += spacing) {
      Point2 pt{x, y};
      if (!point_in_polygon(pt, poly)) continue;
      if (centroid_in_any_hole(pt, holes)) continue;
      output.push_back(pt);
    }
  }
}

} // namespace

SurfaceMeshResult graded_surface_generate(
    const std::vector<Point2>& polygon,
    double near_boundary_size,
    double global_size,
    int boundary_layers,
    double growth_factor,
    int max_nodes,
    const std::vector<std::vector<Point2>>* holes) {
  SurfaceMeshResult r;
  if (polygon.size() < 3) return r;

  near_boundary_size = std::max(near_boundary_size, 1e-6);
  global_size = std::max(global_size, near_boundary_size);
  boundary_layers = std::max(0, std::min(boundary_layers, 12));
  growth_factor = clampd(growth_factor, 1.05, 3.0);

  std::vector<Point2> all_pts;
  all_pts.reserve(512);
  add_boundary_points(polygon, near_boundary_size, all_pts);
  if (holes) {
    for (const auto& hole : *holes) {
      if (hole.size() < 3) continue;
      add_boundary_points(hole, near_boundary_size, all_pts);
    }
  }

  double ring_size = near_boundary_size;
  double offset = 0;
  for (int layer = 0; layer < boundary_layers && static_cast<int>(all_pts.size()) < max_nodes; ++layer) {
    offset += ring_size;
    ring_size *= growth_factor;
    std::vector<Point2> ring;
    if (!compute_inward_offset_polygon(polygon, offset, ring)) break;
    if (ring.size() < 3) break;
    add_boundary_points(ring, ring_size, all_pts);
  }

  add_interior_grid_points(polygon, global_size, all_pts, max_nodes, holes);

  double merge_eps = near_boundary_size * 0.1;
  merge_duplicates(all_pts, merge_eps);
  if (all_pts.size() < 3) return r;

  std::vector<Tri3> tris = bowyer_watson_triangulate(all_pts);
  std::vector<Tri3> kept;
  kept.reserve(tris.size());
  for (const auto& t : tris) {
    double cx = (all_pts[static_cast<size_t>(t.a)].x + all_pts[static_cast<size_t>(t.b)].x +
                 all_pts[static_cast<size_t>(t.c)].x) /
                3;
    double cy = (all_pts[static_cast<size_t>(t.a)].y + all_pts[static_cast<size_t>(t.b)].y +
                 all_pts[static_cast<size_t>(t.c)].y) /
                3;
    Point2 c{cx, cy};
    if (!point_in_polygon(c, polygon)) continue;
    if (centroid_in_any_hole(c, holes)) continue;
    kept.push_back(t);
  }

  double min_a, max_ar;
  compute_triangle_quality_stats(all_pts, kept, min_a, max_ar);
  r.nodes = std::move(all_pts);
  r.triangles = std::move(kept);
  r.min_angle_deg = min_a;
  r.max_aspect_ratio = max_ar;
  return r;
}

void graded_surface_generate_conforming(
    const std::vector<std::tuple<int, std::vector<Point2>, double, double, int, double>>& regions,
    const std::vector<std::tuple<int, std::vector<Point2>, double>>* hole_only,
    int max_nodes,
    std::vector<ConformingRegionResult>& out) {
  out.clear();
  if (regions.empty()) return;

  struct SafeR {
    int sn;
    std::vector<Point2> poly;
    double near_sz, global_sz;
    int layers;
    double growth;
  };
  std::vector<SafeR> safe;
  safe.reserve(regions.size());
  for (const auto& reg : regions) {
    SafeR s;
    s.sn = std::get<0>(reg);
    s.poly = std::get<1>(reg);
    s.near_sz = std::max(std::get<2>(reg), 1e-6);
    s.global_sz = std::max(std::get<3>(reg), s.near_sz);
    s.layers = std::max(0, std::min(std::get<4>(reg), 12));
    s.growth = clampd(std::get<5>(reg), 1.05, 3.0);
    safe.push_back(s);
  }

  std::vector<std::tuple<int, std::vector<Point2>, double>> hole_safe;
  if (hole_only) {
    for (const auto& h : *hole_only) {
      const auto& poly = std::get<1>(h);
      if (poly.size() < 3) continue;
      hole_safe.push_back(std::make_tuple(std::get<0>(h), poly, std::max(std::get<2>(h), 1e-6)));
    }
  }

  std::vector<std::pair<int, std::vector<Point2>>> poly_list;
  for (const auto& r : safe) poly_list.push_back({r.sn, r.poly});
  for (const auto& h : hole_safe) poly_list.push_back({std::get<0>(h), std::get<1>(h)});

  std::map<int, int> parent_map;
  {
    std::vector<std::pair<int, std::vector<Point2>>> pl = poly_list;
    surface_build_parent_map(pl, parent_map);
  }

  auto child_polys_of = [&](int parent_sn) -> std::vector<std::vector<Point2>> {
    std::vector<std::vector<Point2>> list;
    for (const auto& r : safe) {
      auto it = parent_map.find(r.sn);
      if (it != parent_map.end() && it->second == parent_sn) list.push_back(r.poly);
    }
    for (const auto& h : hole_safe) {
      auto it = parent_map.find(std::get<0>(h));
      if (it != parent_map.end() && it->second == parent_sn) list.push_back(std::get<1>(h));
    }
    return list;
  };

  std::vector<Point2> all_pts;
  all_pts.reserve(1024);

  for (const auto& r : safe) {
    add_boundary_points(r.poly, r.near_sz, all_pts);
    double ring_size = r.near_sz;
    double offset = 0;
    for (int layer = 0; layer < r.layers && static_cast<int>(all_pts.size()) < max_nodes; ++layer) {
      offset += ring_size;
      ring_size *= r.growth;
      std::vector<Point2> ring;
      if (!compute_inward_offset_polygon(r.poly, offset, ring)) break;
      if (ring.size() < 3) break;
      add_boundary_points(ring, ring_size, all_pts);
    }
    auto child_polys = child_polys_of(r.sn);
    const std::vector<std::vector<Point2>>* holes_arg =
        child_polys.empty() ? nullptr : &child_polys;
    add_interior_grid_points(r.poly, r.global_sz, all_pts, max_nodes, holes_arg);
  }

  for (const auto& h : hole_safe)
    add_boundary_points(std::get<1>(h), std::get<2>(h), all_pts);

  double merge_eps = safe[0].near_sz * 0.1;
  for (const auto& r : safe) merge_eps = std::min(merge_eps, r.near_sz * 0.1);
  for (const auto& h : hole_safe)
    merge_eps = std::min(merge_eps, std::get<2>(h) * 0.1);

  merge_duplicates(all_pts, merge_eps);
  if (all_pts.size() < 3) return;

  std::vector<Tri3> all_tris = bowyer_watson_triangulate(all_pts);

  for (const auto& r : safe) {
    auto child_polys = child_polys_of(r.sn);
    std::vector<Tri3> region_tris;
    for (const auto& t : all_tris) {
      double cx = (all_pts[static_cast<size_t>(t.a)].x + all_pts[static_cast<size_t>(t.b)].x +
                   all_pts[static_cast<size_t>(t.c)].x) /
                  3;
      double cy = (all_pts[static_cast<size_t>(t.a)].y + all_pts[static_cast<size_t>(t.b)].y +
                   all_pts[static_cast<size_t>(t.c)].y) /
                  3;
      Point2 centroid{cx, cy};
      if (!point_in_polygon(centroid, r.poly)) continue;
      bool in_child = false;
      for (const auto& cp : child_polys) {
        if (point_in_polygon(centroid, cp)) {
          in_child = true;
          break;
        }
      }
      if (in_child) continue;
      region_tris.push_back(t);
    }
    double min_a, max_ar;
    compute_triangle_quality_stats(all_pts, region_tris, min_a, max_ar);
    ConformingRegionResult cr;
    cr.surface_number = r.sn;
    cr.mesh.nodes = all_pts;
    cr.mesh.triangles = std::move(region_tris);
    cr.mesh.min_angle_deg = min_a;
    cr.mesh.max_aspect_ratio = max_ar;
    out.push_back(std::move(cr));
  }
}

} // namespace mesh2d
