#include "polygon_ear.hpp"
#include "mesh2d_geometry.hpp"
#include <cmath>

namespace mesh2d {

namespace {

bool is_convex(const Point2& a, const Point2& b, const Point2& c) {
  return cross2(a, b, c) > 1e-18;
}

bool is_ear(const std::vector<Point2>& vertices, const std::vector<int>& ring, int ear_k,
            int i_prev, int i_curr, int i_next) {
  const Point2& a = vertices[static_cast<size_t>(i_prev)];
  const Point2& b = vertices[static_cast<size_t>(i_curr)];
  const Point2& c = vertices[static_cast<size_t>(i_next)];
  for (int t = 0; t < static_cast<int>(ring.size()); ++t) {
    int vi = ring[static_cast<size_t>(t)];
    if (vi == i_prev || vi == i_curr || vi == i_next) continue;
    if (point_in_triangle(vertices[static_cast<size_t>(vi)], a, b, c)) return false;
  }
  return true;
}

} // namespace

bool polygon_ear_triangulate(const std::vector<Point2>& vertices, std::vector<Tri3>& triangles) {
  triangles.clear();
  int n = static_cast<int>(vertices.size());
  if (n < 3) return false;

  std::vector<int> idx(static_cast<size_t>(n));
  for (int i = 0; i < n; ++i) idx[static_cast<size_t>(i)] = i;

  if (polygon_area_signed(vertices) < 0)
    std::reverse(idx.begin(), idx.end());

  int guard = n * n * 2;
  while (static_cast<int>(idx.size()) > 3 && guard-- > 0) {
    bool removed = false;
    int m = static_cast<int>(idx.size());
    for (int k = 0; k < m; ++k) {
      int i_prev = idx[static_cast<size_t>((k + m - 1) % m)];
      int i_curr = idx[static_cast<size_t>(k)];
      int i_next = idx[static_cast<size_t>((k + 1) % m)];
      const Point2& a = vertices[static_cast<size_t>(i_prev)];
      const Point2& b = vertices[static_cast<size_t>(i_curr)];
      const Point2& c = vertices[static_cast<size_t>(i_next)];
      if (!is_convex(a, b, c)) continue;
      if (!is_ear(vertices, idx, k, i_prev, i_curr, i_next)) continue;
      triangles.push_back({i_prev, i_curr, i_next});
      idx.erase(idx.begin() + k);
      removed = true;
      break;
    }
    if (!removed) return false;
  }

  if (static_cast<int>(idx.size()) == 3)
    triangles.push_back({idx[0], idx[1], idx[2]});
  return !triangles.empty();
}

} // namespace mesh2d
