#ifndef MAIN_COMSUB_HPP
#define MAIN_COMSUB_HPP

#include <vector>
#include <complex>

namespace fem {

/// Complex back-substitution after comred. loads overwritten with solution.
void comsub(const std::vector<std::complex<double>>& bk, std::vector<std::complex<double>>& loads);

} // namespace fem

#endif
