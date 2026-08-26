#ifndef MESH2D_GEOMETRY_HPP
#define MESH2D_GEOMETRY_HPP

#include "mesh2d_types.hpp"
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <vector>

namespace mesh2d {

inline double clampd(double v, double lo, double hi) {
  return v < lo ? lo : (v > hi ? hi : v);
}

inline double cross2(const Point2& a, const Point2& b, const Point2& c) {
  return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
}

inline double dist2(const Point2& a, const Point2& b) {
  double dx = b.x - a.x, dy = b.y - a.y;
  return std::sqrt(dx * dx + dy * dy);
}

inline void normalize2(double& vx, double& vy) {
  double len = std::sqrt(vx * vx + vy * vy);
  if (len < 1e-10) { vx = vy = 0; return; }
  vx /= len;
  vy /= len;
}

/// Ray-casting; skips near-horizontal edges (same as C# GradedSurfaceMeshGenerator).
inline bool point_in_polygon(const Point2& p, const std::vector<Point2>& poly) {
  int n = static_cast<int>(poly.size());
  bool inside = false;
  for (int i = 0, j = n - 1; i < n; j = i++) {
    double xi = poly[static_cast<size_t>(i)].x, yi = poly[static_cast<size_t>(i)].y;
    double xj = poly[static_cast<size_t>(j)].x, yj = poly[static_cast<size_t>(j)].y;
    if (std::fabs(yj - yi) < 1e-12) continue;
    if ((yi > p.y) != (yj > p.y)) {
      double x_int = (xj - xi) * (p.y - yi) / (yj - yi) + xi;
      if (p.x < x_int) inside = !inside;
    }
  }
  return inside;
}

inline double polygon_area_signed(const std::vector<Point2>& v) {
  double a = 0;
  int n = static_cast<int>(v.size());
  for (int i = 0; i < n; ++i) {
    int j = (i + 1) % n;
    a += v[static_cast<size_t>(i)].x * v[static_cast<size_t>(j)].y -
         v[static_cast<size_t>(j)].x * v[static_cast<size_t>(i)].y;
  }
  return a * 0.5;
}

inline double polygon_area(const std::vector<Point2>& poly) {
  return std::fabs(polygon_area_signed(poly));
}

inline Point2 polygon_centroid(const std::vector<Point2>& poly) {
  Point2 c{0, 0};
  if (poly.empty()) return c;
  for (const auto& p : poly) { c.x += p.x; c.y += p.y; }
  c.x /= static_cast<double>(poly.size());
  c.y /= static_cast<double>(poly.size());
  return c;
}

inline bool point_in_triangle(const Point2& p, const Point2& a, const Point2& b, const Point2& c) {
  double s1 = cross2(a, b, p);
  double s2 = cross2(b, c, p);
  double s3 = cross2(c, a, p);
  bool has_neg = (s1 < -1e-12) || (s2 < -1e-12) || (s3 < -1e-12);
  bool has_pos = (s1 > 1e-12) || (s2 > 1e-12) || (s3 > 1e-12);
  return !(has_neg && has_pos);
}

inline void merge_duplicates(std::vector<Point2>& pts, double eps) {
  std::vector<Point2> out;
  double eps2 = eps * eps;
  for (const auto& p : pts) {
    bool dup = false;
    for (const auto& r : out) {
      double dx = p.x - r.x, dy = p.y - r.y;
      if (dx * dx + dy * dy < eps2) { dup = true; break; }
    }
    if (!dup) out.push_back(p);
  }
  pts.swap(out);
}

inline void compute_triangle_quality_stats(
    const std::vector<Point2>& pts,
    const std::vector<Tri3>& tris,
    double& min_angle_deg,
    double& max_aspect_ratio) {
  const double kPi = 3.14159265358979323846;
  min_angle_deg = 180;
  max_aspect_ratio = 0;
  for (const auto& t : tris) {
    const Point2& a = pts[static_cast<size_t>(t.a)];
    const Point2& b = pts[static_cast<size_t>(t.b)];
    const Point2& c = pts[static_cast<size_t>(t.c)];
    double ab = dist2(a, b), bc = dist2(b, c), ca = dist2(c, a);
    if (ab < 1e-12 || bc < 1e-12 || ca < 1e-12) continue;
    double ang_a = std::acos(clampd((ab * ab + ca * ca - bc * bc) / (2 * ab * ca), -1.0, 1.0)) * 180.0 / kPi;
    double ang_b = std::acos(clampd((ab * ab + bc * bc - ca * ca) / (2 * ab * bc), -1.0, 1.0)) * 180.0 / kPi;
    double ang_c = 180 - ang_a - ang_b;
    double m = std::min(ang_a, std::min(ang_b, ang_c));
    min_angle_deg = std::min(min_angle_deg, m);
    double s = (ab + bc + ca) / 2;
    double area = std::fabs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) / 2;
    if (area > 1e-12) {
      double in_r = area / s;
      double ar = std::max(ab, std::max(bc, ca)) / (2 * in_r);
      max_aspect_ratio = std::max(max_aspect_ratio, ar);
    }
  }
  if (tris.empty()) { min_angle_deg = 0; max_aspect_ratio = 0; }
}

} // namespace mesh2d

#endif
