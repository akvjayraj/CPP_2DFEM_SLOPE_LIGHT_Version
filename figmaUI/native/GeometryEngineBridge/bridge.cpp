// Geometry engine: CGAL (2D EPICK + constrained Delaunay) + Open CASCADE (gp/Geom2d metadata)
// Build (example):
//   vcpkg install cgal:x64-windows opencascade:x64-windows
//   cmake -B build -S . -DCMAKE_TOOLCHAIN_FILE=%VCPKG_ROOT%/scripts/buildsystems/vcpkg.cmake
//   cmake --build build --config Release

#include <CGAL/Exact_predicates_inexact_constructions_kernel.h>
#include <CGAL/Polygon_2.h>
#include <CGAL/Constrained_Delaunay_triangulation_2.h>
#include <CGAL/Triangulation_vertex_base_2.h>
#include <CGAL/Triangulation_face_base_2.h>
#include <CGAL/Triangulation_data_structure_2.h>
#include <CGAL/Constrained_triangulation_face_base_2.h>
#include <CGAL/Bounded_side_of_polygon_2.h>

#include <gp_Pnt2d.hxx>
#include <gp_Ax2d.hxx>
#include <gp_Dir2d.hxx>
#include <Geom2d_Circle.hxx>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <sstream>
#include <string>
#include <vector>

typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
typedef CGAL::Triangulation_vertex_base_2<K> Vb;
typedef CGAL::Constrained_triangulation_face_base_2<K> Fb;
typedef CGAL::Triangulation_data_structure_2<Vb, Fb> Tds;
typedef CGAL::Constrained_Delaunay_triangulation_2<K, Tds> CDT;
typedef CGAL::Polygon_2<K> Polygon_2;
typedef K::Point_2 Point_2;

#ifdef _WIN32
#ifdef GEOMETRY_ENGINE_EXPORTS
#define GE_API __declspec(dllexport)
#else
#define GE_API __declspec(dllimport)
#endif
#else
#define GE_API __attribute__((visibility("default")))
#endif

static int write_json(const char* s, char* out, int cap) {
  int n = (int)std::strlen(s);
  if (n + 1 > cap) return -1;
  std::memcpy(out, s, (size_t)n + 1);
  return n;
}

extern "C" {

GE_API void geometry_engine_free_string(char* p) {
  if (p) std::free(p);
}

/// Point: CGAL EPICK + OCCT gp_Pnt2d
GE_API int ge_point_metrics(double x, double y, char* out_json, int out_cap) {
  Point_2 p(x, y);
  gp_Pnt2d occt(x, y);
  char buf[384];
  std::snprintf(buf, sizeof(buf),
                "{\"ok\":true,\"shapeType\":\"point\",\"x\":%.17g,\"y\":%.17g,"
                "\"cgal\":\"EPICK\",\"occt\":{\"gpX\":%.17g,\"gpY\":%.17g}}",
                CGAL::to_double(p.x()), CGAL::to_double(p.y()), occt.X(), occt.Y());
  return write_json(buf, out_json, out_cap);
}

/// Polyline: CGAL length + OCCT endpoints as gp_Pnt2d
GE_API int ge_polyline_metrics(const double* xy, int n_pts, char* out_json, int out_cap) {
  if (n_pts < 2 || !xy) {
    return write_json("{\"ok\":false,\"reason\":\"need at least 2 vertices\"}", out_json, out_cap);
  }
  double len = 0;
  for (int i = 0; i < n_pts - 1; ++i) {
    Point_2 a(xy[i * 2], xy[i * 2 + 1]);
    Point_2 b(xy[(i + 1) * 2], xy[(i + 1) * 2 + 1]);
    len += std::sqrt(CGAL::to_double(CGAL::squared_distance(a, b)));
  }
  gp_Pnt2d p0(xy[0], xy[1]);
  gp_Pnt2d p1(xy[(n_pts - 1) * 2], xy[(n_pts - 1) * 2 + 1]);
  char buf[640];
  std::snprintf(buf, sizeof(buf),
                "{\"ok\":true,\"shapeType\":\"polyline\",\"length\":%.17g,\"segments\":%d,"
                "\"cgal\":\"EPICK length\",\"occt\":{\"start\":{\"x\":%.17g,\"y\":%.17g},\"end\":{\"x\":%.17g,\"y\":%.17g}}}",
                len, n_pts - 1, p0.X(), p0.Y(), p1.X(), p1.Y());
  return write_json(buf, out_json, out_cap);
}

GE_API int ge_polygon_triangulate(const double* xy, int n_pts, char* out_json, int out_cap) {
  if (n_pts < 3 || !xy) {
    return write_json("{\"ok\":false,\"reason\":\"need at least 3 vertices\"}", out_json, out_cap);
  }

  Polygon_2 poly;
  for (int i = 0; i < n_pts; ++i) {
    poly.push_back(Point_2(xy[i * 2], xy[i * 2 + 1]));
  }

  if (!poly.is_simple()) {
    char buf[384];
    std::snprintf(buf, sizeof(buf),
                  "{\"ok\":false,\"shapeType\":\"polygon\",\"isSimple\":false,"
                  "\"reason\":\"polygon is not simple (self-intersection)\"}");
    return write_json(buf, out_json, out_cap);
  }

  double area = std::abs(CGAL::to_double(poly.area()));
  int orient = (poly.orientation() == CGAL::COUNTERCLOCKWISE) ? 1 : -1;

  double cxx = 0, cyy = 0;
  for (int i = 0; i < n_pts; ++i) {
    cxx += xy[i * 2];
    cyy += xy[i * 2 + 1];
  }
  cxx /= (double)n_pts;
  cyy /= (double)n_pts;

  CDT cdt;
  const int nv = (int)poly.size();
  for (int i = 0; i < nv; ++i) {
    cdt.insert_constraint(poly[i], poly[(i + 1) % nv]);
  }

  std::ostringstream tris;
  tris << "[";
  bool first = true;
  int tri_count = 0;

  for (CDT::Finite_faces_iterator fit = cdt.finite_faces_begin(); fit != cdt.finite_faces_end(); ++fit) {
    Point_2 p0 = fit->vertex(0)->point();
    Point_2 p1 = fit->vertex(1)->point();
    Point_2 p2 = fit->vertex(2)->point();
    double tcx = (CGAL::to_double(p0.x()) + CGAL::to_double(p1.x()) + CGAL::to_double(p2.x())) / 3.0;
    double tcy = (CGAL::to_double(p0.y()) + CGAL::to_double(p1.y()) + CGAL::to_double(p2.y())) / 3.0;
    Point_2 c(tcx, tcy);
    if (CGAL::bounded_side_2(poly.vertices_begin(), poly.vertices_end(), c) != CGAL::ON_BOUNDED_SIDE) {
      continue;
    }
    double x0 = CGAL::to_double(p0.x()), y0 = CGAL::to_double(p0.y());
    double x1 = CGAL::to_double(p1.x()), y1 = CGAL::to_double(p1.y());
    double x2 = CGAL::to_double(p2.x()), y2 = CGAL::to_double(p2.y());
    if (!first) tris << ",";
    first = false;
    tris << "[" << x0 << "," << y0 << "," << x1 << "," << y1 << "," << x2 << "," << y2 << "]";
    ++tri_count;
  }
  tris << "]";

  char header[1024];
  std::snprintf(header, sizeof(header),
                "{\"ok\":true,\"shapeType\":\"polygon\",\"isSimple\":true,\"area\":%.17g,"
                "\"orientation\":%d,\"triangleCount\":%d,"
                "\"cgal\":\"EPICK+CDT\",\"occt\":{\"centroidApprox\":{\"x\":%.17g,\"y\":%.17g}},\"triangles\":",
                area, orient, tri_count, cxx, cyy);
  std::string out = std::string(header) + tris.str() + "}";
  return write_json(out.c_str(), out_json, out_cap);
}

GE_API int ge_circle_triangulate(double cx, double cy, double r, int segments, char* out_json, int out_cap) {
  if (r <= 0 || segments < 8) segments = 64;
  if (segments > 256) segments = 256;

  double area = M_PI * r * r;

  gp_Ax2d ax(gp_Pnt2d(cx, cy), gp_Dir2d(1, 0));
  Geom2d_Circle g2d(ax, r);
  double occtR = g2d.Radius();

  std::vector<Point_2> ring;
  ring.reserve((size_t)segments);
  for (int i = 0; i < segments; ++i) {
    double th = 2.0 * M_PI * (double)i / (double)segments;
    ring.push_back(Point_2(cx + r * std::cos(th), cy + r * std::sin(th)));
  }

  Polygon_2 poly(ring.begin(), ring.end());
  double area_poly = std::abs(CGAL::to_double(poly.area()));

  CDT cdt;
  const int nv = (int)poly.size();
  for (int i = 0; i < nv; ++i) {
    cdt.insert_constraint(poly[i], poly[(i + 1) % nv]);
  }

  std::ostringstream tris;
  tris << "[";
  bool first = true;
  int tri_count = 0;
  for (CDT::Finite_faces_iterator fit = cdt.finite_faces_begin(); fit != cdt.finite_faces_end(); ++fit) {
    Point_2 p0 = fit->vertex(0)->point();
    Point_2 p1 = fit->vertex(1)->point();
    Point_2 p2 = fit->vertex(2)->point();
    double tcx = (CGAL::to_double(p0.x()) + CGAL::to_double(p1.x()) + CGAL::to_double(p2.x())) / 3.0;
    double tcy = (CGAL::to_double(p0.y()) + CGAL::to_double(p1.y()) + CGAL::to_double(p2.y())) / 3.0;
    Point_2 c(tcx, tcy);
    if (CGAL::bounded_side_2(poly.vertices_begin(), poly.vertices_end(), c) != CGAL::ON_BOUNDED_SIDE) {
      continue;
    }
    double x0 = CGAL::to_double(p0.x()), y0 = CGAL::to_double(p0.y());
    double x1 = CGAL::to_double(p1.x()), y1 = CGAL::to_double(p1.y());
    double x2 = CGAL::to_double(p2.x()), y2 = CGAL::to_double(p2.y());
    if (!first) tris << ",";
    first = false;
    tris << "[" << x0 << "," << y0 << "," << x1 << "," << y1 << "," << x2 << "," << y2 << "]";
    ++tri_count;
  }
  tris << "]";

  char header[1152];
  std::snprintf(header, sizeof(header),
                "{\"ok\":true,\"shapeType\":\"circle\",\"cx\":%.17g,\"cy\":%.17g,\"r\":%.17g,"
                "\"areaAnalytic\":%.17g,\"areaPolygonApprox\":%.17g,\"segments\":%d,"
                "\"triangleCount\":%d,\"cgal\":\"CDT\",\"occt\":{\"geom2dRadius\":%.17g},\"triangles\":",
                cx, cy, r, area, area_poly, segments, tri_count, occtR);
  std::string out = std::string(header) + tris.str() + "}";
  return write_json(out.c_str(), out_json, out_cap);
}

} // extern "C"
