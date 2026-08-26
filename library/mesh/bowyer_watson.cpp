#include "bowyer_watson.hpp"
#include <algorithm>
#include <cmath>
#include <limits>

namespace mesh2d {

namespace {

struct Tri {
  int a, b, c;
};

bool operator==(const Tri& u, const Tri& v) { return u.a == v.a && u.b == v.b && u.c == v.c; }

bool in_circumcircle(const Point2& a, const Point2& b, const Point2& c, const Point2& p) {
  double ax = a.x - p.x, ay = a.y - p.y;
  double bx = b.x - p.x, by = b.y - p.y;
  double cx = c.x - p.x, cy = c.y - p.y;

  double det = ax * (by * (cx * cx + cy * cy) - cy * (bx * bx + by * by)) -
               ay * (bx * (cx * cx + cy * cy) - cx * (bx * bx + by * by)) +
               (ax * ax + ay * ay) * (bx * cy - by * cx);

  double area2 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
  return area2 >= 0 ? det > 1e-10 : det < -1e-10;
}

void try_add_edge(std::vector<std::pair<int, int>>& boundary, int a, int b) {
  for (size_t i = 0; i < boundary.size(); ++i) {
    int ea = boundary[i].first, eb = boundary[i].second;
    if ((ea == b && eb == a) || (ea == a && eb == b)) {
      boundary.erase(boundary.begin() + static_cast<std::ptrdiff_t>(i));
      return;
    }
  }
  boundary.push_back(std::make_pair(a, b));
}

} // namespace

std::vector<Tri3> bowyer_watson_triangulate(const std::vector<Point2>& input_points) {
  std::vector<Tri3> out;
  int n_in = static_cast<int>(input_points.size());
  if (n_in < 3) return out;

  std::vector<Point2> pts = input_points;
  double min_x = pts[0].x, max_x = pts[0].x, min_y = pts[0].y, max_y = pts[0].y;
  for (int i = 1; i < n_in; ++i) {
    min_x = std::min(min_x, pts[static_cast<size_t>(i)].x);
    max_x = std::max(max_x, pts[static_cast<size_t>(i)].x);
    min_y = std::min(min_y, pts[static_cast<size_t>(i)].y);
    max_y = std::max(max_y, pts[static_cast<size_t>(i)].y);
  }
  double dx = max_x - min_x, dy = max_y - min_y;
  double d = std::max(dx, dy) * 12 + 1;
  double mx = (min_x + max_x) * 0.5, my = (min_y + max_y) * 0.5;

  int n = n_in;
  pts.push_back({mx - 4 * d, my - d});
  pts.push_back({mx, my + 4 * d});
  pts.push_back({mx + 4 * d, my - d});

  std::vector<Tri> triangulation;
  triangulation.push_back({n, n + 1, n + 2});

  std::vector<Tri> bad_tris;
  std::vector<std::pair<int, int>> polygon;

  for (int pi = 0; pi < n_in; ++pi) {
    const Point2& p = pts[static_cast<size_t>(pi)];
    bad_tris.clear();
    polygon.clear();

    for (const auto& t : triangulation) {
      if (in_circumcircle(pts[static_cast<size_t>(t.a)], pts[static_cast<size_t>(t.b)],
                          pts[static_cast<size_t>(t.c)], p))
        bad_tris.push_back(t);
    }

    for (const auto& t : bad_tris) {
      try_add_edge(polygon, t.a, t.b);
      try_add_edge(polygon, t.b, t.c);
      try_add_edge(polygon, t.c, t.a);
    }

    for (const auto& t : bad_tris) {
      auto it = std::find(triangulation.begin(), triangulation.end(), t);
      if (it != triangulation.end()) triangulation.erase(it);
    }

    for (const auto& e : polygon)
      triangulation.push_back({e.first, e.second, pi});
  }

  out.reserve(triangulation.size());
  for (const auto& t : triangulation) {
    if (t.a >= n_in || t.b >= n_in || t.c >= n_in) continue;
    out.push_back({t.a, t.b, t.c});
  }
  return out;
}

} // namespace mesh2d
