#ifndef GEOM_GEOM_RECT_HPP
#define GEOM_GEOM_RECT_HPP

#include <string>
#include <vector>

namespace geom {

/// Forms coordinates and connectivity for rectangular mesh of
/// triangular (3,6,10,15-node) or quadrilateral (4,5,8,9-node) elements.
/// dir='x','r' or 'y'. num and coord are 1-based (Fortran convention).
void geom_rect(const std::string& element, int iel,
               const std::vector<double>& x_coords,
               const std::vector<double>& y_coords,
               std::vector<std::vector<double>>& coord,
               std::vector<int>& num, char dir);

} // namespace geom

#endif
