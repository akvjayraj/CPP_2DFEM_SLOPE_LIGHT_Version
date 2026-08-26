#ifndef GEOM_FORMNF_HPP
#define GEOM_FORMNF_HPP

#include <vector>

namespace geom {

/// Forms the nf matrix: assigns equation numbers (1,2,3,...) to free DOFs.
/// nf is column-major (nodof x nn), 0 = fixed.
void formnf(std::vector<int>& nf, int nodof, int nn);

} // namespace geom

#endif
