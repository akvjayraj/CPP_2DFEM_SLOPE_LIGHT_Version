#ifndef MAIN_SHAPE_DER_HPP
#define MAIN_SHAPE_DER_HPP

#include <vector>

namespace fem {

/// Derivatives of shape functions w.r.t. local coordinates at point i.
/// points: nip x ndim. der: output, ndim x nod.
void shape_der(std::vector<std::vector<double>>& der,
               const std::vector<std::vector<double>>& points, int i);

} // namespace fem

#endif
