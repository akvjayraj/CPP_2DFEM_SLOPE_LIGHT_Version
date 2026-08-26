#ifndef MAIN_DEEMAT_HPP
#define MAIN_DEEMAT_HPP

#include <vector>

namespace fem {

/// Returns the elastic dee matrix. ih=3: plane strain, ih=4: axisymmetry,
/// ih=6: 3D. dee dimensions determine ih.
void deemat(std::vector<std::vector<double>>& dee, double e, double v);

} // namespace fem

#endif
