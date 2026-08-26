#ifndef MAIN_COMRED_HPP
#define MAIN_COMRED_HPP

#include <vector>
#include <complex>

namespace fem {

/// Gaussian reduction on complex vector-stored upper triangle (skyline). Modifies bk in place.
void comred(std::vector<std::complex<double>>& bk, int n);

} // namespace fem

#endif
