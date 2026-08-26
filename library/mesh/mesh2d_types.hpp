#ifndef MESH2D_TYPES_HPP
#define MESH2D_TYPES_HPP

namespace mesh2d {

struct Point2 {
  double x, y;
};

struct Tri3 {
  int a, b, c;
};

struct Tri6 {
  int n1, n2, n3, n4, n5, n6;
};

} // namespace mesh2d

#endif
