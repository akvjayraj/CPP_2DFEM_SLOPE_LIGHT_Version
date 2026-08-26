#ifndef MAIN_FSPARV_HPP
#define MAIN_FSPARV_HPP

#include <vector>

namespace fem {

/// Assembles element matrix into symmetric skyline global matrix.
void fsparv(std::vector<double>& kv, const double km[2][2],
            const std::vector<int>& g, const std::vector<int>& kdiag);

/// Overload for general ndof x ndof element matrix.
void fsparv(std::vector<double>& kv,
            const std::vector<std::vector<double>>& km,
            const std::vector<int>& g, const std::vector<int>& kdiag);

} // namespace fem

#endif
