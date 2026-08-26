#include "geom_rect_triangle.hpp"
#include <algorithm>
#include <cmath>

namespace mesh2d {

int geom_rect_element_count(int nxe, int nye_physical) { return nxe * nye_physical * 2; }

int geom_rect_node_count(int nxe, int nye_physical) { return (nxe + 1) * (nye_physical + 1); }

void geom_rect_get_triangle_node_ids(int iel, int nxe, int nye_physical, char dir, int& n1, int& n2, int& n3) {
  int nye = nye_physical * 2;
  if (dir == 'x' || dir == 'r') {
    int jel = 2 * nxe * ((iel - 1) / (2 * nxe));
    int ip = std::max(1, (iel - jel) / 2);
    int iq = 2 * ((iel - 1) / (2 * nxe) + 1) - 1 + ((iel / 2) * 2) / std::max(iel, 1);
    if (iq % 2 != 0) {
      n1 = (nxe + 1) * (iq - 1) / 2 + ip;
      n2 = n1 + 1;
      n3 = (nxe + 1) * (iq + 1) / 2 + ip;
    } else {
      n1 = (nxe + 1) * iq / 2 + ip + 1;
      n2 = n1 - 1;
      n3 = (nxe + 1) * (iq - 2) / 2 + ip + 1;
    }
    return;
  }
  if (dir == 'y') {
    int jel = (iel - 1) / nye;
    int ip = jel + 1;
    int iq = iel - nye * jel;
    if (iq % 2 != 0) {
      n1 = (ip - 1) * (nye + 2) / 2 + (iq + 1) / 2;
      n2 = n1 + (nye + 2) / 2;
      n3 = n1 + 1;
    } else {
      n1 = ip * (nye + 2) / 2 + (iq + 2) / 2;
      n2 = (ip - 1) * (nye + 2) / 2 + (iq + 1) / 2 + 1;
      n3 = n1 - 1;
    }
    return;
  }
  n1 = n2 = n3 = 0;
}

void geom_rect_triangle_build(const std::vector<double>& x_coords, const std::vector<double>& y_coords,
                              char dir, GeomRectTriangleMeshResult& out) {
  out = GeomRectTriangleMeshResult();
  if (x_coords.size() < 2 || y_coords.size() < 2) return;

  int nxe = static_cast<int>(x_coords.size()) - 1;
  int nye_physical = static_cast<int>(y_coords.size()) - 1;
  int nn = geom_rect_node_count(nxe, nye_physical);
  int nels = geom_rect_element_count(nxe, nye_physical);

  out.nxe = nxe;
  out.nye = nye_physical;
  out.node_points.resize(static_cast<size_t>(nn));
  for (int i = 0; i < nn; ++i) {
    int iy = i / (nxe + 1);
    int ix = i % (nxe + 1);
    double x = ix < static_cast<int>(x_coords.size()) ? x_coords[static_cast<size_t>(ix)] : 0;
    double y = iy < static_cast<int>(y_coords.size()) ? y_coords[static_cast<size_t>(iy)] : 0;
    out.node_points[static_cast<size_t>(i)] = {x, y};
  }

  out.triangles_1based.reserve(static_cast<size_t>(nels));
  for (int iel = 1; iel <= nels; ++iel) {
    int t1, t2, t3;
    geom_rect_get_triangle_node_ids(iel, nxe, nye_physical, dir, t1, t2, t3);
    if (t1 == 0 && t2 == 0 && t3 == 0) {
      out = GeomRectTriangleMeshResult();
      return;
    }
    out.triangles_1based.push_back({t1, t2, t3});
  }
}

} // namespace mesh2d
