#include "t6_upgrade.hpp"

namespace mesh2d {

std::vector<Tri6> t6_upgrade_mesh(std::vector<Point2>& nodes, const std::vector<Tri3>& t3_triangles) {
  std::map<std::pair<int, int>, int> edge_to_mid;
  std::vector<Tri6> t6;
  t6.reserve(t3_triangles.size());

  auto get_or_add_mid = [&](int i, int j) -> int {
    int lo = i < j ? i : j, hi = i < j ? j : i;
    auto key = std::make_pair(lo, hi);
    auto it = edge_to_mid.find(key);
    if (it != edge_to_mid.end()) return it->second;
    int mid = static_cast<int>(nodes.size());
    const Point2& a = nodes[static_cast<size_t>(i)];
    const Point2& b = nodes[static_cast<size_t>(j)];
    nodes.push_back({(a.x + b.x) * 0.5, (a.y + b.y) * 0.5});
    edge_to_mid[key] = mid;
    return mid;
  };

  for (const auto& t : t3_triangles) {
    int n4 = get_or_add_mid(t.a, t.b);
    int n5 = get_or_add_mid(t.b, t.c);
    int n6 = get_or_add_mid(t.c, t.a);
    t6.push_back({t.a, t.b, t.c, n4, n5, n6});
  }
  return t6;
}

} // namespace mesh2d
