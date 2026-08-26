#ifndef MAIN_GAUSS_BAND_HPP
#define MAIN_GAUSS_BAND_HPP

#include <vector>

namespace fem {

/// Gaussian reduction of unsymmetric band matrix pb (in-place). work used as working space.
void gauss_band(std::vector<std::vector<double>>& pb, std::vector<std::vector<double>>& work);

} // namespace fem

#endif
