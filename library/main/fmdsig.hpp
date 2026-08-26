#ifndef MAIN_FMDSIG_HPP
#define MAIN_FMDSIG_HPP

#include <vector>

namespace fem {

/// Elastic D matrix for plane stress (ih=3).
void fmdsig(std::vector<std::vector<double>>& dee, double e, double v);

} // namespace fem

#endif
