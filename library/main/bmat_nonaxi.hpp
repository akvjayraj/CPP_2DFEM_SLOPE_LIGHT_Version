#ifndef MAIN_BMAT_NONAXI_HPP
#define MAIN_BMAT_NONAXI_HPP

#include <vector>

namespace fem {

void bmat_nonaxi(std::vector<std::vector<double>>& bee, double& radius,
                 const std::vector<std::vector<double>>& coord, const std::vector<std::vector<double>>& deriv,
                 const std::vector<double>& fun, int iflag, int lth);

} // namespace fem

#endif
