#include "tunnel_mesh.hpp"
#include "bowyer_watson.hpp"
#include "mesh2d_geometry.hpp"
#include <algorithm>
#include <cmath>

namespace mesh2d {

namespace {

void sample_polygon_edges(const std::vector<Point2>& poly, double spacing, std::vector<Point2>& output) {
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

void sample_circle(const Point2& center, double radius, double spacing, std::vector<Point2>& output) {
  int n = std::max(8, static_cast<int>(std::ceil(2.0 * 3.14159265358979323846 * radius / spacing)));
  for (int i = 0; i < n; ++i) {
    double theta = i * 2.0 * 3.14159265358979323846 / n;
    output.push_back({center.x + radius * std::cos(theta), center.y + radius * std::sin(theta)});
  }
}

bool compute_outward_offset_polygon(const std::vector<Point2>& hole, double dist,
                                    const std::vector<Point2>& outer_bound, std::vector<Point2>& res) {
  int n = static_cast<int>(hole.size());
  res.clear();
  res.reserve(static_cast<size_t>(n));
  for (int i = 0; i < n; ++i) {
    const Point2& prev = hole[static_cast<size_t>((i + n - 1) % n)];
    const Point2& cur = hole[static_cast<size_t>(i)];
    const Point2& next = hole[static_cast<size_t>((i + 1) % n)];
    double d1x = cur.x - prev.x, d1y = cur.y - prev.y;
    normalize2(d1x, d1y);
    double n1x = d1y, n1y = -d1x;
    n1x = -n1x;
    n1y = -n1y;
    double d2x = next.x - cur.x, d2y = next.y - cur.y;
    normalize2(d2x, d2y);
    double n2x = d2y, n2y = -d2x;
    n2x = -n2x;
    n2y = -n2y;
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
      scale = std::min(scale, dist * 4);
      px = cur.x + bx * scale;
      py = cur.y + by * scale;
    }
    res.push_back({px, py});
  }
  for (const auto& p : res) {
    if (!point_in_polygon(p, outer_bound)) return false;
  }
  return true;
}

bool circle_fully_inside(const Point2& center, double r, const std::vector<Point2>& poly) {
  for (int i = 0; i < 8; ++i) {
    double theta = i * 3.14159265358979323846 / 4;
    Point2 p{center.x + r * std::cos(theta), center.y + r * std::sin(theta)};
    if (!point_in_polygon(p, poly)) return false;
  }
  return true;
}

void add_interior_grid(const std::vector<Point2>& outer,
                       const std::vector<std::pair<Point2, double>>& circles,
                       const std::vector<std::vector<Point2>>& poly_holes, double spacing,
                       std::vector<Point2>& output, int max_nodes) {
  double min_x = outer[0].x, max_x = outer[0].x, min_y = outer[0].y, max_y = outer[0].y;
  for (size_t i = 1; i < outer.size(); ++i) {
    min_x = std::min(min_x, outer[i].x);
    max_x = std::max(max_x, outer[i].x);
    min_y = std::min(min_y, outer[i].y);
    max_y = std::max(max_y, outer[i].y);
  }
  for (double y = min_y + spacing * 0.5; y < max_y && static_cast<int>(output.size()) < max_nodes;
       y += spacing) {
    for (double x = min_x + spacing * 0.5; x < max_x && static_cast<int>(output.size()) < max_nodes;
         x += spacing) {
      Point2 pt{x, y};
      if (!point_in_polygon(pt, outer)) continue;
      bool in_circle = false;
      for (const auto& c : circles) {
        double dx = pt.x - c.first.x, dy = pt.y - c.first.y;
        if (dx * dx + dy * dy <= c.second * c.second) {
          in_circle = true;
          break;
        }
      }
      if (in_circle) continue;
      bool in_poly_hole = false;
      for (const auto& h : poly_holes) {
        if (h.size() >= 3 && point_in_polygon(pt, h)) {
          in_poly_hole = true;
          break;
        }
      }
      if (in_poly_hole) continue;
      output.push_back(pt);
    }
  }
}

void merge_dup(std::vector<Point2>& pts, double eps) {
  merge_duplicates(pts, eps);
}

void compute_stats(const std::vector<Point2>& pts, const std::vector<Tri3>& tris, double& min_a, double& max_ar) {
  compute_triangle_quality_stats(pts, tris, min_a, max_ar);
}

} // namespace

TunnelMeshResult tunnel_mesh_generate(
    const std::vector<Point2>& outer_polygon,
    const std::vector<std::pair<Point2, double>>& circle_holes,
    const std::vector<std::vector<Point2>>& polygon_holes,
    double far_field_edge_length,
    double near_boundary_size,
    int ring_count,
    double ring_growth_factor,
    double first_ring_thickness,
    int max_nodes) {
  TunnelMeshResult r;
  if (outer_polygon.size() < 3) return r;

  far_field_edge_length = std::max(far_field_edge_length, 1e-6);
  near_boundary_size = std::max(near_boundary_size, 1e-6);
  first_ring_thickness = std::max(first_ring_thickness, near_boundary_size * 0.5);
  ring_count = std::max(0, std::min(ring_count, 20));
  ring_growth_factor = clampd(ring_growth_factor, 1.05, 3.0);

  std::vector<std::pair<Point2, double>> circle_defs;
  for (const auto& c : circle_holes) {
    if (c.second > 1e-9) circle_defs.push_back(c);
  }

  std::vector<std::vector<Point2>> valid_poly_holes;
  for (const auto& h : polygon_holes) {
    if (h.size() >= 3) valid_poly_holes.push_back(h);
  }

  std::vector<Point2> all_pts;
  all_pts.reserve(4096);
  sample_polygon_edges(outer_polygon, near_boundary_size, all_pts);

  for (const auto& cd : circle_defs) {
    if (static_cast<int>(all_pts.size()) >= max_nodes) break;
    const Point2& center = cd.first;
    double radius = cd.second;
    sample_circle(center, radius, near_boundary_size, all_pts);
    double spacing = near_boundary_size;
    double ring_r = radius + first_ring_thickness;
    for (int k = 0; k < ring_count && static_cast<int>(all_pts.size()) < max_nodes; ++k) {
      if (spacing >= far_field_edge_length) break;
      if (!circle_fully_inside(center, ring_r, outer_polygon)) break;
      sample_circle(center, ring_r, spacing, all_pts);
      ring_r += spacing;
      spacing *= ring_growth_factor;
    }
  }

  for (const auto& hole : valid_poly_holes) {
    if (static_cast<int>(all_pts.size()) >= max_nodes) break;
    sample_polygon_edges(hole, near_boundary_size, all_pts);
    double spacing = near_boundary_size;
    double offset = first_ring_thickness;
    for (int k = 0; k < ring_count && static_cast<int>(all_pts.size()) < max_nodes; ++k) {
      if (spacing >= far_field_edge_length) break;
      std::vector<Point2> ring;
      if (!compute_outward_offset_polygon(hole, offset, outer_polygon, ring)) break;
      if (ring.size() < 3) break;
      sample_polygon_edges(ring, spacing, all_pts);
      offset += spacing;
      spacing *= ring_growth_factor;
    }
  }

  add_interior_grid(outer_polygon, circle_defs, valid_poly_holes, far_field_edge_length, all_pts, max_nodes);

  double merge_eps = near_boundary_size * 0.05;
  merge_dup(all_pts, merge_eps);
  if (all_pts.size() < 3) return r;

  std::vector<Tri3> all_tris = bowyer_watson_triangulate(all_pts);
  std::vector<Tri3> active_tris, tunnel_tris;

  for (const auto& t : all_tris) {
    double cx = (all_pts[static_cast<size_t>(t.a)].x + all_pts[static_cast<size_t>(t.b)].x +
                 all_pts[static_cast<size_t>(t.c)].x) /
                3;
    double cy = (all_pts[static_cast<size_t>(t.a)].y + all_pts[static_cast<size_t>(t.b)].y +
                 all_pts[static_cast<size_t>(t.c)].y) /
                3;
    Point2 c{cx, cy};
    if (!point_in_polygon(c, outer_polygon)) continue;
    bool in_circle_hole = false;
    for (const auto& cd : circle_defs) {
      double dx = c.x - cd.first.x, dy = c.y - cd.first.y;
      if (dx * dx + dy * dy <= cd.second * cd.second * 1.001 * 1.001) {
        in_circle_hole = true;
        break;
      }
    }
    bool in_poly_hole = false;
    for (const auto& h : valid_poly_holes) {
      if (point_in_polygon(c, h)) {
        in_poly_hole = true;
        break;
      }
    }
    if (in_circle_hole || in_poly_hole) tunnel_tris.push_back(t);
    else active_tris.push_back(t);
  }

  std::vector<Tri3> combined = active_tris;
  combined.insert(combined.end(), tunnel_tris.begin(), tunnel_tris.end());
  double min_a, max_ar;
  compute_stats(all_pts, combined, min_a, max_ar);

  r.nodes = std::move(all_pts);
  r.active_triangles = std::move(active_tris);
  r.tunnel_triangles = std::move(tunnel_tris);
  r.min_angle_deg = min_a;
  r.max_aspect_ratio = max_ar;
  return r;
}

} // namespace mesh2d
