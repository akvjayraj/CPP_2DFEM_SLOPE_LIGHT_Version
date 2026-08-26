#include "mesh2d_c_api.h"
#include "geom_rect_triangle.hpp"
#include "graded_surface_mesh.hpp"
#include "polygon_ear.hpp"
#include "surface_mesh_hierarchy.hpp"
#include "t6_upgrade.hpp"
#include "tunnel_mesh.hpp"
#include "mesh2d_geometry.hpp"

#include <cstdlib>
#include <cstring>
#include <vector>

namespace {

bool read_i32(const unsigned char*& p, const unsigned char* end, int& v) {
  if (p + 4 > end) return false;
  std::memcpy(&v, p, 4);
  p += 4;
  return true;
}

bool read_d(const unsigned char*& p, const unsigned char* end, double& v) {
  if (p + 8 > end) return false;
  std::memcpy(&v, p, 8);
  p += 8;
  return true;
}

void write_i32(std::vector<unsigned char>& b, int v) {
  unsigned char tmp[4];
  std::memcpy(tmp, &v, 4);
  b.insert(b.end(), tmp, tmp + 4);
}

void write_d(std::vector<unsigned char>& b, double v) {
  unsigned char tmp[8];
  std::memcpy(tmp, &v, 8);
  b.insert(b.end(), tmp, tmp + 8);
}

} // namespace

extern "C" {

void mesh2d_free_doubles(double* p) { std::free(p); }
void mesh2d_free_ints(int* p) { std::free(p); }
void mesh2d_free_bytes(unsigned char* p) { std::free(p); }

void mesh2d_geom_rect_triangle_ids(int iel, int nxe, int nye_physical, char dir, int* n1, int* n2, int* n3) {
  if (!n1 || !n2 || !n3) return;
  int a = 0, b = 0, c = 0;
  mesh2d::geom_rect_get_triangle_node_ids(iel, nxe, nye_physical, dir, a, b, c);
  *n1 = a;
  *n2 = b;
  *n3 = c;
}

int mesh2d_geom_rect_build(const double* x, int nx, const double* y, int ny, char dir, double** out_xy,
                           int* out_nn, int** out_tris, int* out_ne) {
  if (!x || !y || nx < 2 || ny < 2 || !out_xy || !out_nn || !out_tris || !out_ne) return -1;
  std::vector<double> xv(x, x + nx);
  std::vector<double> yv(y, y + ny);
  mesh2d::GeomRectTriangleMeshResult res;
  mesh2d::geom_rect_triangle_build(xv, yv, dir, res);
  if (res.node_points.empty() || res.triangles_1based.empty()) return -1;
  int nn = static_cast<int>(res.node_points.size());
  int ne = static_cast<int>(res.triangles_1based.size());
  double* xy = static_cast<double*>(std::malloc(sizeof(double) * static_cast<size_t>(nn) * 2));
  int* tr = static_cast<int*>(std::malloc(sizeof(int) * static_cast<size_t>(ne) * 3));
  if (!xy || !tr) {
    std::free(xy);
    std::free(tr);
    return -1;
  }
  for (int i = 0; i < nn; ++i) {
    xy[2 * i] = res.node_points[static_cast<size_t>(i)].x;
    xy[2 * i + 1] = res.node_points[static_cast<size_t>(i)].y;
  }
  for (int e = 0; e < ne; ++e) {
    tr[3 * e] = res.triangles_1based[static_cast<size_t>(e)].a;
    tr[3 * e + 1] = res.triangles_1based[static_cast<size_t>(e)].b;
    tr[3 * e + 2] = res.triangles_1based[static_cast<size_t>(e)].c;
  }
  *out_xy = xy;
  *out_nn = nn;
  *out_tris = tr;
  *out_ne = ne;
  return 0;
}

int mesh2d_polygon_ear(const double* xy, int npt, int** out_tris, int* out_nt) {
  if (!xy || npt < 3 || !out_tris || !out_nt) return -1;
  std::vector<mesh2d::Point2> v(static_cast<size_t>(npt));
  for (int i = 0; i < npt; ++i) v[static_cast<size_t>(i)] = {xy[2 * i], xy[2 * i + 1]};
  std::vector<mesh2d::Tri3> tris;
  if (!mesh2d::polygon_ear_triangulate(v, tris) || tris.empty()) return -1;
  int nt = static_cast<int>(tris.size());
  int* t = static_cast<int*>(std::malloc(sizeof(int) * static_cast<size_t>(nt) * 3));
  if (!t) return -1;
  for (int i = 0; i < nt; ++i) {
    t[3 * i] = tris[static_cast<size_t>(i)].a;
    t[3 * i + 1] = tris[static_cast<size_t>(i)].b;
    t[3 * i + 2] = tris[static_cast<size_t>(i)].c;
  }
  *out_tris = t;
  *out_nt = nt;
  return 0;
}

double mesh2d_polygon_area(const double* xy, int npt) {
  if (!xy || npt < 3) return 0;
  std::vector<mesh2d::Point2> v(static_cast<size_t>(npt));
  for (int i = 0; i < npt; ++i) v[static_cast<size_t>(i)] = {xy[2 * i], xy[2 * i + 1]};
  return mesh2d::surface_polygon_area(v);
}

int mesh2d_tunnel_generate(const double* outer_xy, int n_outer, const double* circles, int n_circles,
                           const int* poly_hole_offsets, int n_poly_holes, const double* poly_holes_xy,
                           double far_h, double near_h, int rings, double rgrowth, double first_thick, int max_nodes,
                           double** out_xy, int* nn, int** out_active_tris, int* n_active, int** out_tunnel_tris,
                           int* n_tunnel) {
  if (!outer_xy || n_outer < 3 || !out_xy || !nn || !out_active_tris || !n_active || !out_tunnel_tris ||
      !n_tunnel)
    return -1;
  std::vector<mesh2d::Point2> outer(static_cast<size_t>(n_outer));
  for (int i = 0; i < n_outer; ++i) outer[static_cast<size_t>(i)] = {outer_xy[2 * i], outer_xy[2 * i + 1]};
  std::vector<std::pair<mesh2d::Point2, double>> circ;
  if (circles && n_circles > 0) {
    for (int i = 0; i < n_circles; ++i)
      circ.push_back({{circles[3 * i], circles[3 * i + 1]}, circles[3 * i + 2]});
  }
  std::vector<std::vector<mesh2d::Point2>> poly_holes;
  if (poly_hole_offsets && poly_holes_xy && n_poly_holes > 0) {
    for (int h = 0; h < n_poly_holes; ++h) {
      int a = poly_hole_offsets[h], b = poly_hole_offsets[h + 1];
      int nv = (b - a) / 2;
      std::vector<mesh2d::Point2> hole(static_cast<size_t>(nv));
      for (int i = 0; i < nv; ++i)
        hole[static_cast<size_t>(i)] = {poly_holes_xy[static_cast<size_t>(a + 2 * i)],
                                         poly_holes_xy[static_cast<size_t>(a + 2 * i + 1)]};
      poly_holes.push_back(std::move(hole));
    }
  }
  mesh2d::TunnelMeshResult r =
      mesh2d::tunnel_mesh_generate(outer, circ, poly_holes, far_h, near_h, rings, rgrowth, first_thick, max_nodes);
  if (r.nodes.empty()) return -1;
  int nnode = static_cast<int>(r.nodes.size());
  double* xy = static_cast<double*>(std::malloc(sizeof(double) * static_cast<size_t>(nnode) * 2));
  if (!xy) return -1;
  for (int i = 0; i < nnode; ++i) {
    xy[2 * i] = r.nodes[static_cast<size_t>(i)].x;
    xy[2 * i + 1] = r.nodes[static_cast<size_t>(i)].y;
  }
  int na = static_cast<int>(r.active_triangles.size());
  int nt = static_cast<int>(r.tunnel_triangles.size());
  int* ta = static_cast<int*>(std::malloc(sizeof(int) * static_cast<size_t>(std::max(1, na)) * 3));
  int* tt = static_cast<int*>(std::malloc(sizeof(int) * static_cast<size_t>(std::max(1, nt)) * 3));
  if (!ta || !tt) {
    std::free(xy);
    std::free(ta);
    std::free(tt);
    return -1;
  }
  for (int i = 0; i < na; ++i) {
    ta[3 * i] = r.active_triangles[static_cast<size_t>(i)].a;
    ta[3 * i + 1] = r.active_triangles[static_cast<size_t>(i)].b;
    ta[3 * i + 2] = r.active_triangles[static_cast<size_t>(i)].c;
  }
  for (int i = 0; i < nt; ++i) {
    tt[3 * i] = r.tunnel_triangles[static_cast<size_t>(i)].a;
    tt[3 * i + 1] = r.tunnel_triangles[static_cast<size_t>(i)].b;
    tt[3 * i + 2] = r.tunnel_triangles[static_cast<size_t>(i)].c;
  }
  *out_xy = xy;
  *nn = nnode;
  *out_active_tris = ta;
  *n_active = na;
  *out_tunnel_tris = tt;
  *n_tunnel = nt;
  return 0;
}

int mesh2d_t6_upgrade(const double* xy, int nn, const int* t3, int nt3, double** out_xy, int* nn_out, int** out_t6,
                      int* nt6) {
  if (!xy || nn < 3 || !t3 || nt3 < 1 || !out_xy || !nn_out || !out_t6 || !nt6) return -1;
  std::vector<mesh2d::Point2> nodes(static_cast<size_t>(nn));
  for (int i = 0; i < nn; ++i) nodes[static_cast<size_t>(i)] = {xy[2 * i], xy[2 * i + 1]};
  std::vector<mesh2d::Tri3> t3v(static_cast<size_t>(nt3));
  for (int i = 0; i < nt3; ++i)
    t3v[static_cast<size_t>(i)] = {t3[3 * i], t3[3 * i + 1], t3[3 * i + 2]};
  std::vector<mesh2d::Tri6> t6 = mesh2d::t6_upgrade_mesh(nodes, t3v);
  int nno = static_cast<int>(nodes.size());
  double* oxy = static_cast<double*>(std::malloc(sizeof(double) * static_cast<size_t>(nno) * 2));
  int* ot = static_cast<int*>(std::malloc(sizeof(int) * static_cast<size_t>(nt3) * 6));
  if (!oxy || !ot) {
    std::free(oxy);
    std::free(ot);
    return -1;
  }
  for (int i = 0; i < nno; ++i) {
    oxy[2 * i] = nodes[static_cast<size_t>(i)].x;
    oxy[2 * i + 1] = nodes[static_cast<size_t>(i)].y;
  }
  for (int i = 0; i < nt3; ++i) {
    ot[6 * i] = t6[static_cast<size_t>(i)].n1;
    ot[6 * i + 1] = t6[static_cast<size_t>(i)].n2;
    ot[6 * i + 2] = t6[static_cast<size_t>(i)].n3;
    ot[6 * i + 3] = t6[static_cast<size_t>(i)].n4;
    ot[6 * i + 4] = t6[static_cast<size_t>(i)].n5;
    ot[6 * i + 5] = t6[static_cast<size_t>(i)].n6;
  }
  *out_xy = oxy;
  *nn_out = nno;
  *out_t6 = ot;
  *nt6 = nt3;
  return 0;
}

int mesh2d_graded_conforming(const unsigned char* blob, int blob_len, unsigned char** out_blob, int* out_len) {
  if (!blob || blob_len < 4 || !out_blob || !out_len) return -1;
  const unsigned char* p = blob;
  const unsigned char* end = blob + blob_len;
  int n_reg = 0;
  if (!read_i32(p, end, n_reg) || n_reg < 1) return -1;
  std::vector<std::tuple<int, std::vector<mesh2d::Point2>, double, double, int, double>> regions;
  for (int r = 0; r < n_reg; ++r) {
    int sn = 0, nvert = 0;
    if (!read_i32(p, end, sn) || !read_i32(p, end, nvert) || nvert < 3) return -1;
    std::vector<mesh2d::Point2> poly(static_cast<size_t>(nvert));
    for (int i = 0; i < nvert; ++i) {
      double x, y;
      if (!read_d(p, end, x) || !read_d(p, end, y)) return -1;
      poly[static_cast<size_t>(i)] = {x, y};
    }
    double near_sz, glob_sz, growth;
    int layers;
    if (!read_d(p, end, near_sz) || !read_d(p, end, glob_sz) || !read_d(p, end, growth) || !read_i32(p, end, layers))
      return -1;
    regions.push_back(std::make_tuple(sn, poly, near_sz, glob_sz, layers, growth));
  }
  int n_hole = 0;
  if (!read_i32(p, end, n_hole) || n_hole < 0) return -1;
  std::vector<std::tuple<int, std::vector<mesh2d::Point2>, double>> holes;
  for (int h = 0; h < n_hole; ++h) {
    int sn = 0, nvert = 0;
    if (!read_i32(p, end, sn) || !read_i32(p, end, nvert) || nvert < 3) return -1;
    std::vector<mesh2d::Point2> poly(static_cast<size_t>(nvert));
    for (int i = 0; i < nvert; ++i) {
      double x, y;
      if (!read_d(p, end, x) || !read_d(p, end, y)) return -1;
      poly[static_cast<size_t>(i)] = {x, y};
    }
    double hn;
    if (!read_d(p, end, hn)) return -1;
    holes.push_back(std::make_tuple(sn, poly, hn));
  }
  int max_nodes = 0;
  if (!read_i32(p, end, max_nodes)) return -1;

  const std::vector<std::tuple<int, std::vector<mesh2d::Point2>, double>>* hp =
      holes.empty() ? nullptr : &holes;
  std::vector<mesh2d::ConformingRegionResult> results;
  mesh2d::graded_surface_generate_conforming(regions, hp, max_nodes, results);
  if (results.empty()) return -1;

  const auto& shared_nodes = results[0].mesh.nodes;
  int nn = static_cast<int>(shared_nodes.size());

  std::vector<unsigned char> out;
  write_i32(out, nn);
  for (int i = 0; i < nn; ++i) {
    write_d(out, shared_nodes[static_cast<size_t>(i)].x);
    write_d(out, shared_nodes[static_cast<size_t>(i)].y);
  }
  int nres = static_cast<int>(results.size());
  write_i32(out, nres);
  for (const auto& cr : results) {
    write_i32(out, cr.surface_number);
    int nt = static_cast<int>(cr.mesh.triangles.size());
    write_i32(out, nt);
    for (int t = 0; t < nt; ++t) {
      write_i32(out, cr.mesh.triangles[static_cast<size_t>(t)].a);
      write_i32(out, cr.mesh.triangles[static_cast<size_t>(t)].b);
      write_i32(out, cr.mesh.triangles[static_cast<size_t>(t)].c);
    }
    write_d(out, cr.mesh.min_angle_deg);
    write_d(out, cr.mesh.max_aspect_ratio);
  }

  *out_len = static_cast<int>(out.size());
  *out_blob = static_cast<unsigned char*>(std::malloc(out.size()));
  if (!*out_blob) return -1;
  std::memcpy(*out_blob, out.data(), out.size());
  return 0;
}

} // extern "C"
