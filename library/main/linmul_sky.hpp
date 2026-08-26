#ifndef MAIN_LINMUL_SKY_HPP
#define MAIN_LINMUL_SKY_HPP

#include <vector>

namespace fem {

/// Forms the product of symmetric skyline matrix kv and vector disps.
/// loads = kv * disps.
void linmul_sky(const std::vector<double>& kv, const std::vector<double>& disps,
                std::vector<double>& loads, const std::vector<int>& kdiag);

} // namespace fem

#endif
