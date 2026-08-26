#ifndef MAIN_SHAPE_FUN_HPP
#define MAIN_SHAPE_FUN_HPP

#include <vector>

namespace fem {

/// Computes shape function values at integration point i.
/// points: nip x ndim. fun: output, size nod.
void shape_fun(std::vector<double>& fun, const std::vector<std::vector<double>>& points, int i);

} // namespace fem

#endif
