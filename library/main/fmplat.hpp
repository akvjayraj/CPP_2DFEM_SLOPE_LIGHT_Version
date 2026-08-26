#ifndef MAIN_FMPLAT_HPP
#define MAIN_FMPLAT_HPP

#include <vector>

namespace fem {

/// Forms the 2nd derivatives for rectangular plate bending elements.
/// points: nip x 2. d2x, d2y, d2xy: size ndof (16).
void fmplat(const std::vector<std::vector<double>>& points, double aa, double bb,
            int i, std::vector<double>& d2x, std::vector<double>& d2y,
            std::vector<double>& d2xy);

} // namespace fem

#endif
